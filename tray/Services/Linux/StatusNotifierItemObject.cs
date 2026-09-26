using System.Text;
using Tmds.DBus.Protocol;

namespace Tvnoms.Tray.Services.Linux;

/// <summary>Icon pixels as a StatusNotifierItem pixmap: ARGB32, network byte order.</summary>
public sealed record TrayPixmap(int Width, int Height, byte[] Argb);

/// <summary>The <c>org.kde.StatusNotifierItem</c> object panels query for the icon, tooltip and
/// menu. This is the whole reason the tray has its own Linux implementation: it advertises
/// <c>ItemIsMenu=true</c> and has no <c>Activate</c> method, the same shape libayatana-appindicator
/// items (e.g. Remmina) have. Hosts then open the menu on left click as well as right click — KDE
/// and waybar via ItemIsMenu, others by falling back to the menu when Activate is unknown.
/// Avalonia's own tray icon hardcodes ItemIsMenu=false and exposes Activate, so left click on it
/// does nothing on most panels.
///
/// Called on Tmds.DBus's own thread, so state is swapped as whole immutable snapshots.</summary>
internal sealed class StatusNotifierItemObject(DBusConnection connection) : IPathMethodHandler {
  private const string Interface = "org.kde.StatusNotifierItem";
  private const string PixmapSignature = "a(iiay)";
  private const string ToolTipSignature = "(sa(iiay)ss)";

  private static readonly ReadOnlyMemory<byte>[] IntrospectionXml = [
    Encoding.UTF8.GetBytes("""
      <interface name="org.kde.StatusNotifierItem">
        <method name="ContextMenu"><arg type="i" direction="in"/><arg type="i" direction="in"/></method>
        <method name="SecondaryActivate"><arg type="i" direction="in"/><arg type="i" direction="in"/></method>
        <method name="Scroll"><arg type="i" direction="in"/><arg type="s" direction="in"/></method>
        <signal name="NewTitle"/>
        <signal name="NewIcon"/>
        <signal name="NewToolTip"/>
        <signal name="NewStatus"><arg type="s"/></signal>
        <property name="Category" type="s" access="read"/>
        <property name="Id" type="s" access="read"/>
        <property name="Title" type="s" access="read"/>
        <property name="Status" type="s" access="read"/>
        <property name="WindowId" type="i" access="read"/>
        <property name="IconThemePath" type="s" access="read"/>
        <property name="IconName" type="s" access="read"/>
        <property name="IconPixmap" type="a(iiay)" access="read"/>
        <property name="OverlayIconName" type="s" access="read"/>
        <property name="OverlayIconPixmap" type="a(iiay)" access="read"/>
        <property name="AttentionIconName" type="s" access="read"/>
        <property name="AttentionIconPixmap" type="a(iiay)" access="read"/>
        <property name="AttentionMovieName" type="s" access="read"/>
        <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
        <property name="ItemIsMenu" type="b" access="read"/>
        <property name="Menu" type="o" access="read"/>
      </interface>
      """),
  ];

  private static readonly string[] PropertyNames = [
    "Category", "Id", "Title", "Status", "WindowId", "IconThemePath", "IconName", "IconPixmap",
    "OverlayIconName", "OverlayIconPixmap", "AttentionIconName", "AttentionIconPixmap",
    "AttentionMovieName", "ToolTip", "ItemIsMenu", "Menu",
  ];

  private volatile TrayPixmap? _icon;
  private volatile string _title = "Tvnoms";
  private volatile string _toolTipTitle = "Tvnoms";
  private volatile string _toolTipText = "";

  public const string ObjectPath = "/StatusNotifierItem";
  public string Path => ObjectPath;
  public bool HandlesChildPaths => false;

  public void SetIcon(TrayPixmap? icon) {
    _icon = icon;
    EmitSignal("NewIcon");
  }

  /// <summary>The first line becomes the tooltip heading (and the item's Title, which some hosts
  /// show instead of the tooltip); any further lines become the tooltip body.</summary>
  public void SetToolTip(string text) {
    var lines = text.Split('\n', 2);
    _title = lines[0];
    _toolTipTitle = lines[0];
    _toolTipText = lines.Length > 1 ? lines[1] : "";
    EmitSignal("NewTitle");
    EmitSignal("NewToolTip");
  }

  private void EmitSignal(string member) {
    using var writer = connection.GetMessageWriter();
    writer.WriteSignalHeader(null, ObjectPath, Interface, member, null);
    connection.TrySendMessage(writer.CreateMessage());
  }

  public ValueTask HandleMethodAsync(MethodContext context) {
    if (context.IsDBusIntrospectRequest) {
      context.ReplyIntrospectXml(IntrospectionXml, Array.Empty<string>());
    } else if (context.IsPropertiesInterfaceRequest) {
      HandleProperties(context);
    } else if (context.Request.InterfaceAsString == Interface &&
               context.Request.MemberAsString is "ContextMenu" or "SecondaryActivate" or "Scroll") {
      // The panel shows the menu itself (via the Menu property); nothing to do on our side.
      if (!context.NoReplyExpected) {
        using var writer = context.CreateReplyWriter(null!);
        context.Reply(writer.CreateMessage());
      }
    } else {
      // Includes Activate, deliberately — see the class summary.
      context.ReplyUnknownMethodError();
    }
    return ValueTask.CompletedTask;
  }

  private void HandleProperties(MethodContext context) {
    var reader = context.Request.GetBodyReader();
    switch (context.Request.MemberAsString) {
      case "Get": {
        reader.ReadString();
        var name = reader.ReadString();
        if (!PropertyNames.Contains(name)) {
          context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", $"No property '{name}'");
          return;
        }
        var writer = context.CreateReplyWriter("v");
        try {
          WriteProperty(ref writer, name);
          context.Reply(writer.CreateMessage());
        } finally {
          writer.Dispose();
        }
        return;
      }
      case "GetAll": {
        var writer = context.CreateReplyWriter("a{sv}");
        try {
          var dict = writer.WriteDictionaryStart();
          foreach (var name in PropertyNames) {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(name);
            WriteProperty(ref writer, name);
          }
          writer.WriteDictionaryEnd(dict);
          context.Reply(writer.CreateMessage());
        } finally {
          writer.Dispose();
        }
        return;
      }
      default:
        context.ReplyError("org.freedesktop.DBus.Error.PropertyReadOnly", "Properties are read-only");
        return;
    }
  }

  /// <summary>Writes one property as a variant (signature, then value).</summary>
  private void WriteProperty(ref MessageWriter writer, string name) {
    switch (name) {
      case "Category": writer.WriteVariantString("ApplicationStatus"); break;
      case "Id": writer.WriteVariantString("tvnoms-tray"); break;
      case "Title": writer.WriteVariantString(_title); break;
      case "Status": writer.WriteVariantString("Active"); break;
      case "WindowId": writer.WriteVariantInt32(0); break;
      case "ItemIsMenu": writer.WriteVariantBool(true); break;
      case "Menu": writer.WriteVariantObjectPath(DbusMenuObject.ObjectPath); break;
      case "IconPixmap":
        writer.WriteSignature(PixmapSignature);
        WritePixmaps(ref writer, _icon);
        break;
      case "OverlayIconPixmap" or "AttentionIconPixmap":
        writer.WriteSignature(PixmapSignature);
        WritePixmaps(ref writer, null);
        break;
      case "ToolTip":
        writer.WriteSignature(ToolTipSignature);
        writer.WriteStructureStart();
        writer.WriteString("");
        WritePixmaps(ref writer, null);
        writer.WriteString(_toolTipTitle);
        writer.WriteString(_toolTipText);
        break;
      default:
        // IconThemePath, IconName, OverlayIconName, AttentionIconName, AttentionMovieName.
        writer.WriteVariantString("");
        break;
    }
  }

  private static void WritePixmaps(ref MessageWriter writer, TrayPixmap? pixmap) {
    var array = writer.WriteArrayStart(DBusType.Struct);
    if (pixmap is not null) {
      writer.WriteStructureStart();
      writer.WriteInt32(pixmap.Width);
      writer.WriteInt32(pixmap.Height);
      writer.WriteArray(pixmap.Argb);
    }
    writer.WriteArrayEnd(array);
  }
}
