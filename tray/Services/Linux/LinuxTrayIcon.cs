using System.Collections.Specialized;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Tmds.DBus.Protocol;

namespace Tvnoms.Tray.Services.Linux;

/// <summary>Linux tray icon published straight over D-Bus (StatusNotifierItem + dbusmenu) in
/// place of Avalonia's, so the menu opens on left click — see <see cref="StatusNotifierItemObject"/>
/// for why Avalonia's can't.
///
/// App.axaml stays the single definition of the tray: the Avalonia TrayIcon declared there is
/// kept hidden on Linux, its bindings keep running against the view model as usual, and this class
/// mirrors its Icon, ToolTipText and Menu onto the bus whenever they change.</summary>
public sealed class LinuxTrayIcon : IDisposable {
  private const string WatcherService = "org.kde.StatusNotifierWatcher";

  private readonly TrayIcon _source;
  private readonly DBusConnection _connection;
  private readonly StatusNotifierItemObject _item;
  private readonly DbusMenuObject _menu;
  private readonly string _serviceName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";
  private readonly List<NativeMenuItem> _observedItems = [];
  private IDisposable? _watcherSubscription;
  private bool _menuUpdateQueued;

  private LinuxTrayIcon(TrayIcon source, DBusConnection connection) {
    _source = source;
    _connection = connection;
    _item = new StatusNotifierItemObject(connection);
    _menu = new DbusMenuObject(connection);
    _menu.Clicked += id => Dispatcher.UIThread.Post(() => OnMenuClicked(id));
  }

  /// <summary>Returns null when there's no usable session bus, so the caller can fall back to
  /// Avalonia's own tray icon.</summary>
  public static async Task<LinuxTrayIcon?> TryStartAsync(TrayIcon source) {
    if (DBusAddress.Session is not { } address) {
      return null;
    }

    var connection = new DBusConnection(address);
    try {
      await connection.ConnectAsync();
      var tray = new LinuxTrayIcon(source, connection);
      await tray.StartAsync();
      return tray;
    } catch (Exception ex) {
      Console.WriteLine($"[tvnoms-tray] D-Bus tray unavailable, falling back to Avalonia's: {ex.Message}");
      connection.Dispose();
      return null;
    }
  }

  private async Task StartAsync() {
    _connection.AddMethodHandlers([_item, _menu]);
    await _connection.RequestNameAsync(_serviceName, RequestNameOptions.None);

    MirrorIcon();
    MirrorToolTip();
    ObserveMenu();
    _source.PropertyChanged += OnSourcePropertyChanged;

    // Panels come and go (a panel restart, or the session's panel starting after us at login) —
    // re-register with whichever watcher owns the name each time it changes hands.
    _watcherSubscription = await _connection.WatchSignalAsync(
      "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameOwnerChanged",
      static (message, _) => {
        var reader = message.GetBodyReader();
        return (Name: reader.ReadString(), OldOwner: reader.ReadString(), NewOwner: reader.ReadString());
      },
      notification => {
        if (notification.Type == NotificationType.Value &&
            notification.Value is { Name: WatcherService, NewOwner.Length: > 0 }) {
          _ = RegisterWithWatcherAsync();
        }
      },
      ObserverFlags.None, emitOnCapturedContext: false);

    await RegisterWithWatcherAsync();
  }

  private async Task RegisterWithWatcherAsync() {
    try {
      await _connection.CallMethodAsync(CreateRegisterMessage());
    } catch (DBusErrorReplyException) {
      // No panel with a system tray is running yet; NameOwnerChanged registers us once one is.
    }
  }

  private MessageBuffer CreateRegisterMessage() {
    using var writer = _connection.GetMessageWriter();
    writer.WriteMethodCallHeader(WatcherService, "/StatusNotifierWatcher", WatcherService,
      "RegisterStatusNotifierItem", "s");
    writer.WriteString(_serviceName);
    return writer.CreateMessage();
  }

  private void OnSourcePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
    if (e.Property == TrayIcon.IconProperty) {
      MirrorIcon();
    } else if (e.Property == TrayIcon.ToolTipTextProperty) {
      MirrorToolTip();
    } else if (e.Property == TrayIcon.MenuProperty) {
      ObserveMenu();
    }
  }

  private void MirrorIcon() {
    TrayPixmap? pixmap = null;
    try {
      pixmap = _source.Icon is { } icon ? ToPixmap(icon) : null;
    } catch (Exception ex) {
      Console.WriteLine($"[tvnoms-tray] Couldn't convert tray icon: {ex.Message}");
    }
    _item.SetIcon(pixmap);
  }

  private void MirrorToolTip() => _item.SetToolTip(_source.ToolTipText ?? "");

  private void ObserveMenu() {
    foreach (var item in _observedItems) {
      item.PropertyChanged -= OnMenuItemPropertyChanged;
    }
    _observedItems.Clear();

    if (_source.Menu is { } menu) {
      if (menu.Items is INotifyCollectionChanged items) {
        items.CollectionChanged -= OnMenuItemsChanged;
        items.CollectionChanged += OnMenuItemsChanged;
      }
      foreach (var item in menu.Items.OfType<NativeMenuItem>()) {
        item.PropertyChanged += OnMenuItemPropertyChanged;
        _observedItems.Add(item);
      }
    }
    PublishMenu();
  }

  private void OnMenuItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ObserveMenu();

  // Several bound properties tend to change together (e.g. CanStartProxy and CanStopProxy), so
  // coalesce them into one layout update.
  private void OnMenuItemPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
    if (_menuUpdateQueued) {
      return;
    }
    _menuUpdateQueued = true;
    Dispatcher.UIThread.Post(() => {
      _menuUpdateQueued = false;
      PublishMenu();
    });
  }

  private void PublishMenu() {
    var items = _source.Menu?.Items.OfType<NativeMenuItem>().ToArray() ?? [];
    _menu.SetEntries(items.Select((item, index) => item is NativeMenuItemSeparator
      ? new TrayMenuEntry(index + 1, true, "", true, true)
      : new TrayMenuEntry(index + 1, false, item.Header ?? "", item.IsEnabled, item.IsVisible)).ToArray());
  }

  private void OnMenuClicked(int id) {
    var items = _source.Menu?.Items.OfType<NativeMenuItem>().ToArray() ?? [];
    if (id < 1 || id > items.Length || items[id - 1] is NativeMenuItemSeparator) {
      return;
    }

    var item = items[id - 1];
    if (item.IsEnabled && item.Command is { } command && command.CanExecute(item.CommandParameter)) {
      command.Execute(item.CommandParameter);
    }
  }

  /// <summary>Round-trips the icon through an encoded stream into a known pixel layout, since
  /// WindowIcon exposes no pixels directly.</summary>
  private static TrayPixmap ToPixmap(WindowIcon icon) {
    using var stream = new MemoryStream();
    icon.Save(stream);
    stream.Position = 0;

    using var bitmap = new Bitmap(stream);
    var size = bitmap.PixelSize;
    using var converted = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
    using var framebuffer = converted.Lock();
    bitmap.CopyPixels(framebuffer);

    var row = new byte[size.Width * 4];
    var argb = new byte[size.Width * size.Height * 4];
    for (var y = 0; y < size.Height; y++) {
      Marshal.Copy(framebuffer.Address + y * framebuffer.RowBytes, row, 0, row.Length);
      for (var x = 0; x < size.Width; x++) {
        var src = x * 4;
        var dst = (y * size.Width + x) * 4;
        argb[dst] = row[src + 3];     // A
        argb[dst + 1] = row[src + 2]; // R
        argb[dst + 2] = row[src + 1]; // G
        argb[dst + 3] = row[src];     // B
      }
    }
    return new TrayPixmap(size.Width, size.Height, argb);
  }

  public void Dispose() {
    _source.PropertyChanged -= OnSourcePropertyChanged;
    foreach (var item in _observedItems) {
      item.PropertyChanged -= OnMenuItemPropertyChanged;
    }
    _watcherSubscription?.Dispose();
    // Dropping the connection drops our bus name, which is how the watcher learns the item is gone.
    _connection.Dispose();
  }
}
