using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Tvnoms.Tray.Services.Linux;
using Tvnoms.Tray.ViewModels;
using Tvnoms.Tray.Views;

namespace Tvnoms.Tray;

public class App : Application {
  private const string PickAutostartModeArg = "--pick-autostart-mode";

  public override void Initialize() {
    AvaloniaXamlLoader.Load(this);
  }

  public override void OnFrameworkInitializationCompleted() {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
      var pickerOutputPath = FindPickerOutputPath(desktop.Args);
      if (pickerOutputPath is not null) {
        ShowAutostartPicker(desktop, pickerOutputPath);
      } else {
        // No main window is ever created in normal tray-icon mode — the tray icon is the entire UI.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        IgnoreTrayIconShutdownCancellation();
        DataContext = new TrayViewModel(desktop);
        ShowTrayIcon(desktop);
      }
    }

    base.OnFrameworkInitializationCompleted();
  }

  // A one-shot invocation used only by the proxy's Velopack OnFirstRun hook (see
  // UpdateManager.PromptForAutostartMode on the proxy side): show the picker, let the user's
  // choice write itself to outputPath, then exit as soon as that window closes. This never
  // reaches the normal tray-icon startup above.
  private static string? FindPickerOutputPath(string[]? args) {
    if (args is null) {
      return null;
    }

    var flagIndex = Array.IndexOf(args, PickAutostartModeArg);
    return flagIndex >= 0 && flagIndex + 1 < args.Length ? args[flagIndex + 1] : null;
  }

  private static void ShowAutostartPicker(IClassicDesktopStyleApplicationLifetime desktop, string outputPath) {
    desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
    var window = new AutostartChoiceWindow {
      DataContext = new AutostartChoiceViewModel(outputPath)
    };
    desktop.MainWindow = window;
    window.Show();
  }

  private void ShowTrayIcon(IClassicDesktopStyleApplicationLifetime desktop) {
    if (TrayIcon.GetIcons(this) is not { Count: > 0 } icons) {
      return;
    }

    var trayIcon = icons[0];
    if (OperatingSystem.IsLinux()) {
      _ = PublishLinuxTrayIconAsync(desktop, trayIcon);
      return;
    }

    trayIcon.IsVisible = true;
    OpenMenuOnLeftClick(trayIcon);
  }

  private static async Task PublishLinuxTrayIconAsync(IClassicDesktopStyleApplicationLifetime desktop, TrayIcon trayIcon) {
    var linuxTrayIcon = await LinuxTrayIcon.TryStartAsync(trayIcon);
    if (linuxTrayIcon is null) {
      // No session bus we can use ourselves - Avalonia's icon is still better than none, even if
      // its menu only opens on right click.
      trayIcon.IsVisible = true;
      return;
    }
    desktop.Exit += (_, _) => linuxTrayIcon.Dispose();
  }

  // Windows only opens a tray icon's menu on right click, and Avalonia has no public API to open it
  // any other way - but its Win32 TrayIconImpl builds and shows the menu (at the cursor) in a
  // private OnRightClicked, so left click calls that too. Best-effort: if a future Avalonia renames
  // it, left click just goes back to doing nothing. (Linux gets the same behaviour from
  // LinuxTrayIcon instead.)
  private static void OpenMenuOnLeftClick(TrayIcon trayIcon) {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var impl = typeof(TrayIcon)
      .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
      .Select(f => f.GetValue(trayIcon))
      .FirstOrDefault(v => v?.GetType().Name == "TrayIconImpl");
    var showMenu = impl?.GetType().GetMethod("OnRightClicked", BindingFlags.Instance | BindingFlags.NonPublic);
    if (showMenu is null) {
      return;
    }

    trayIcon.Clicked += (_, _) => showMenu.Invoke(impl, null);
  }

  // On Linux, Avalonia's D-Bus tray icon cancels its own watcher when it's disposed at shutdown,
  // and that TaskCanceledException surfaces on the dispatcher as unhandled. Left alone it aborts
  // the process (SIGABRT), which systemd's Restart=on-failure treats as a crash - so quitting from
  // the tray menu just brought the tray straight back. Only that one exception is swallowed; any
  // other unhandled exception still crashes (and restarts) as before.
  private static void IgnoreTrayIconShutdownCancellation() {
    Dispatcher.UIThread.UnhandledException += (_, e) => {
      if (e.Exception is OperationCanceledException &&
          e.Exception.StackTrace?.Contains("DBusTrayIconImpl") == true) {
        e.Handled = true;
      }
    };
  }
}
