using System.Text;
using Tmds.DBus.Protocol;

namespace Tvnoms.Tray.Services.Linux;

/// <summary>One menu row as the panel sees it. Ids are 1-based positions in the menu; 0 is the
/// implicit root.</summary>
public sealed record TrayMenuEntry(int Id, bool IsSeparator, string Label, bool IsEnabled, bool IsVisible);

/// <summary>The <c>com.canonical.dbusmenu</c> object the tray's StatusNotifierItem points its
/// <c>Menu</c> property at. The panel owns rendering, so all this does is describe a flat list of
/// rows and report clicks back. Any change republishes the whole layout (LayoutUpdated) rather
/// than diffing per-item properties — the menu is a handful of rows, and every host supports it.
///
/// Called on Tmds.DBus's own thread, so state is swapped as whole immutable snapshots.</summary>
internal sealed class DbusMenuObject(DBusConnection connection) : IPathMethodHandler {
  private const string Interface = "com.canonical.dbusmenu";
  private const string LayoutSignature = "(ia{sv}av)";

  private static readonly ReadOnlyMemory<byte>[] IntrospectionXml = [
    Encoding.UTF8.GetBytes("""
      <interface name="com.canonical.dbusmenu">
        <method name="GetLayout"><arg type="i" direction="in"/><arg type="i" direction="in"/><arg type="as" direction="in"/><arg type="u" direction="out"/><arg type="(ia{sv}av)" direction="out"/></method>
        <method name="GetGroupProperties"><arg type="ai" direction="in"/><arg type="as" direction="in"/><arg type="a(ia{sv})" direction="out"/></method>
        <method name="GetProperty"><arg type="i" direction="in"/><arg type="s" direction="in"/><arg type="v" direction="out"/></method>
        <method name="Event"><arg type="i" direction="in"/><arg type="s" direction="in"/><arg type="v" direction="in"/><arg type="u" direction="in"/></method>
        <method name="EventGroup"><arg type="a(isvu)" direction="in"/><arg type="ai" direction="out"/></method>
        <method name="AboutToShow"><arg type="i" direction="in"/><arg type="b" direction="out"/></method>
        <method name="AboutToShowGroup"><arg type="ai" direction="in"/><arg type="ai" direction="out"/><arg type="ai" direction="out"/></method>
        <signal name="ItemsPropertiesUpdated"><arg type="a(ia{sv})"/><arg type="a(ias)"/></signal>
        <signal name="LayoutUpdated"><arg type="u"/><arg type="i"/></signal>
        <signal name="ItemActivationRequested"><arg type="i"/><arg type="u"/></signal>
        <property name="Version" type="u" access="read"/>
        <property name="TextDirection" type="s" access="read"/>
        <property name="Status" type="s" access="read"/>
        <property name="IconThemePath" type="as" access="read"/>
      </interface>
      """),
  ];

  private volatile TrayMenuEntry[] _entries = [];
  private uint _revision = 1;

  public const string ObjectPath = "/MenuBar";
  public string Path => ObjectPath;
  public bool HandlesChildPaths => false;

  /// <summary>Raised (on the D-Bus thread) when the user clicks a row.</summary>
  public event Action<int>? Clicked;

  public void SetEntries(TrayMenuEntry[] entries) {
    _entries = entries;
    var revision = Interlocked.Increment(ref _revision);

    using var writer = connection.GetMessageWriter();
    writer.WriteSignalHeader(null, ObjectPath, Interface, "LayoutUpdated", "ui");
    writer.WriteUInt32(revision);
    writer.WriteInt32(0);
    connection.TrySendMessage(writer.CreateMessage());
  }

  public ValueTask HandleMethodAsync(MethodContext context) {
    if (context.IsDBusIntrospectRequest) {
      context.ReplyIntrospectXml(IntrospectionXml, Array.Empty<string>());
      return ValueTask.CompletedTask;
    }
    if (context.IsPropertiesInterfaceRequest) {
      HandleProperties(context);
      return ValueTask.CompletedTask;
    }
    if (context.Request.InterfaceAsString != Interface) {
      context.ReplyUnknownMethodError();
      return ValueTask.CompletedTask;
    }

    var reader = context.Request.GetBodyReader();
    switch (context.Request.MemberAsString) {
      case "GetLayout": {
        var parentId = reader.ReadInt32();
        var writer = context.CreateReplyWriter("u" + LayoutSignature);
        try {
          writer.WriteUInt32(_revision);
          WriteLayout(ref writer, _entries, parentId, withChildren: true);
          context.Reply(writer.CreateMessage());
        } finally {
          writer.Dispose();
        }
        break;
      }
      case "GetGroupProperties": {
        var ids = reader.ReadArrayOfInt32();
        var entries = _entries;
        var writer = context.CreateReplyWriter("a(ia{sv})");
        try {
          var array = writer.WriteArrayStart(DBusType.Struct);
          foreach (var id in ids.Length > 0 ? ids : entries.Select(e => e.Id).Prepend(0)) {
            writer.WriteStructureStart();
            writer.WriteInt32(id);
            writer.WriteDictionary(PropertiesOf(entries, id));
          }
          writer.WriteArrayEnd(array);
          context.Reply(writer.CreateMessage());
        } finally {
          writer.Dispose();
        }
        break;
      }
      case "GetProperty": {
        var id = reader.ReadInt32();
        var name = reader.ReadString();
        var property = PropertiesOf(_entries, id).FirstOrDefault(p => p.Key == name);
        if (property.Key is null) {
          context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"No property '{name}' on item {id}");
          break;
        }
        using var writer = context.CreateReplyWriter("v");
        writer.WriteVariant(property.Value);
        context.Reply(writer.CreateMessage());
        break;
      }
      case "Event": {
        var id = reader.ReadInt32();
        var eventId = reader.ReadString();
        if (eventId == "clicked") {
          Clicked?.Invoke(id);
        }
        ReplyEmpty(context);
        break;
      }
      case "EventGroup": {
        var events = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(events)) {
          reader.AlignStruct();
          var id = reader.ReadInt32();
          var eventId = reader.ReadString();
          reader.ReadVariantValue();
          reader.ReadUInt32();
          if (eventId == "clicked") {
            Clicked?.Invoke(id);
          }
        }
        using var writer = context.CreateReplyWriter("ai");
        writer.WriteArray(Array.Empty<int>());
        context.Reply(writer.CreateMessage());
        break;
      }
      case "AboutToShow": {
        using var writer = context.CreateReplyWriter("b");
        writer.WriteBool(false);
        context.Reply(writer.CreateMessage());
        break;
      }
      case "AboutToShowGroup": {
        using var writer = context.CreateReplyWriter("aiai");
        writer.WriteArray(Array.Empty<int>());
        writer.WriteArray(Array.Empty<int>());
        context.Reply(writer.CreateMessage());
        break;
      }
      default:
        context.ReplyUnknownMethodError();
        break;
    }
    return ValueTask.CompletedTask;
  }

  private static void WriteLayout(ref MessageWriter writer, TrayMenuEntry[] entries, int id, bool withChildren) {
    writer.WriteStructureStart();
    writer.WriteInt32(id);
    writer.WriteDictionary(PropertiesOf(entries, id));
    var children = writer.WriteArrayStart(DBusType.Variant);
    if (withChildren && id == 0) {
      foreach (var entry in entries) {
        writer.WriteSignature(LayoutSignature);
        WriteLayout(ref writer, entries, entry.Id, withChildren: false);
      }
    }
    writer.WriteArrayEnd(children);
  }

  private static KeyValuePair<string, VariantValue>[] PropertiesOf(TrayMenuEntry[] entries, int id) {
    if (id == 0) {
      return [new("children-display", VariantValue.String("submenu"))];
    }

    var entry = entries.FirstOrDefault(e => e.Id == id);
    if (entry is null) {
      return [];
    }
    if (entry.IsSeparator) {
      return [
        new("type", VariantValue.String("separator")),
        new("visible", VariantValue.Bool(entry.IsVisible)),
      ];
    }
    return [
      // dbusmenu treats a single underscore as a mnemonic marker, so literal ones are doubled.
      new("label", VariantValue.String(entry.Label.Replace("_", "__"))),
      new("enabled", VariantValue.Bool(entry.IsEnabled)),
      new("visible", VariantValue.Bool(entry.IsVisible)),
    ];
  }

  private static void HandleProperties(MethodContext context) {
    var reader = context.Request.GetBodyReader();
    switch (context.Request.MemberAsString) {
      case "Get": {
        reader.ReadString();
        var name = reader.ReadString();
        if (!TryGetProperty(name, out var value)) {
          context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", $"No property '{name}'");
          return;
        }
        using var writer = context.CreateReplyWriter("v");
        writer.WriteVariant(value);
        context.Reply(writer.CreateMessage());
        return;
      }
      case "GetAll": {
        using var writer = context.CreateReplyWriter("a{sv}");
        writer.WriteDictionary(MenuProperties());
        context.Reply(writer.CreateMessage());
        return;
      }
      default:
        context.ReplyError("org.freedesktop.DBus.Error.PropertyReadOnly", "Properties are read-only");
        return;
    }
  }

  private static KeyValuePair<string, VariantValue>[] MenuProperties() => [
    new("Version", VariantValue.UInt32(3)),
    new("TextDirection", VariantValue.String("ltr")),
    new("Status", VariantValue.String("normal")),
    new("IconThemePath", VariantValue.Array(Array.Empty<string>())),
  ];

  private static bool TryGetProperty(string name, out VariantValue value) {
    var match = MenuProperties().FirstOrDefault(p => p.Key == name);
    value = match.Value;
    return match.Key is not null;
  }

  private static void ReplyEmpty(MethodContext context) {
    if (context.NoReplyExpected) {
      return;
    }
    using var writer = context.CreateReplyWriter(null!);
    context.Reply(writer.CreateMessage());
  }
}
