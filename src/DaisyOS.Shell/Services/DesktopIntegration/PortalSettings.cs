using Avalonia;
using Avalonia.Styling;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.DesktopIntegration;

internal sealed class PortalSettings(DBusConnection bus)
{
    public const string Interface = "org.freedesktop.impl.portal.Settings";
    private readonly Dictionary<string, uint> _last = [];
    private static uint ColorScheme => Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? 1u : 2u;
    public void Update()
    {
        foreach (var (key, value) in Values())
        {
            if (_last.TryGetValue(key, out var previous) && previous == value) continue;
            _last[key] = value;
            using var writer = bus.GetMessageWriter();
            writer.WriteSignalHeader(path: "/org/freedesktop/portal/desktop", @interface: Interface, member: "SettingChanged", signature: "ssv");
            writer.WriteString("org.freedesktop.appearance"); writer.WriteString(key); writer.WriteVariant(value); bus.TrySendMessage(writer.CreateMessage());
        }
    }
    private static Dictionary<string, uint> Values() => new()
    {
        ["color-scheme"] = ColorScheme,
        ["contrast"] = App.Current is App { HighContrastEnabled: true } ? 1u : 0u,
        ["reduced-motion"] = App.Current is App { ReduceMotionEnabled: true } ? 1u : 0u
    };
    public static Action ReplyFor(MethodContext context)
    {
        var body = context.Request.GetBodyReader();
        var properties = Values().ToDictionary(pair => pair.Key, pair => (VariantValue)pair.Value);
        if (context.Request.MemberAsString == "Read" && context.Request.SignatureAsString == "ss")
        {
            var ns = body.ReadString(); var key = body.ReadString();
            return () =>
            {
                if (ns != "org.freedesktop.appearance" || !properties.TryGetValue(key, out var value))
                { context.ReplyError("org.freedesktop.portal.Error.NotFound", "Unknown setting."); return; }
                using var writer = context.CreateReplyWriter("v"); writer.WriteVariant(value); context.Reply(writer.CreateMessage());
            };
        }
        if (context.Request.MemberAsString == "ReadAll" && context.Request.SignatureAsString == "as")
        {
            var namespaces = body.ReadArrayOfString();
            var include = namespaces.Length == 0 || namespaces.Any(ns => ns is "*" or "org.freedesktop.*" or "org.freedesktop.appearance");
            return () =>
            {
                using var writer = context.CreateReplyWriter("a{sa{sv}}");
                var dictionary = writer.WriteDictionaryStart();
                if (include) { writer.WriteDictionaryEntryStart(); writer.WriteString("org.freedesktop.appearance"); writer.WriteDictionary(properties); }
                writer.WriteDictionaryEnd(dictionary); context.Reply(writer.CreateMessage());
            };
        }
        return () => context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", "Unknown settings method.");
    }
}
