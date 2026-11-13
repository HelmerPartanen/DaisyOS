using Avalonia;
using Avalonia.Styling;
using DaisyOS.Shell.Services.Theming;
using DaisyOS.System.Appearance;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.DesktopIntegration;

internal sealed class PortalSettings(DBusConnection bus)
{
    public const string Interface = "org.freedesktop.impl.portal.Settings";
    private readonly Dictionary<(string Namespace, string Key), VariantValue> _last = [];
    private static uint ColorScheme => Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? 1u : 2u;
    public void Update()
    {
        foreach (var (ns, properties) in Values())
        {
            foreach (var (key, value) in properties)
            {
                if (_last.TryGetValue((ns, key), out var previous) && previous.Equals(value)) continue;
                _last[(ns, key)] = value;
                using var writer = bus.GetMessageWriter();
                writer.WriteSignalHeader(path: "/org/freedesktop/portal/desktop", @interface: Interface, member: "SettingChanged", signature: "ssv");
                writer.WriteString(ns); writer.WriteString(key); writer.WriteVariant(value); bus.TrySendMessage(writer.CreateMessage());
            }
        }
    }

    internal static Dictionary<string, Dictionary<string, VariantValue>> Values() => new()
    {
        ["org.freedesktop.appearance"] = new()
        {
            ["color-scheme"] = ColorScheme,
            ["contrast"] = App.Current is App { HighContrastEnabled: true } ? 1u : 0u,
            ["reduced-motion"] = App.Current is App { ReduceMotionEnabled: true } ? 1u : 0u
        },
        // GTK's Wayland backend reads this namespace through the portal. Without
        // text-scaling-factor it can report an unset (-1) GDK font DPI, which
        // WebKitGTK turns into a negative viewport and a collapsed page layout.
        ["org.gnome.desktop.interface"] = new()
        {
            ["text-scaling-factor"] = Math.Clamp(App.Current is App app ? app.CurrentTextScalePercent : 100, 90, 150) / 100.0,
            ["font-name"] = (App.Current is App current ? current.CurrentSystemFontFamily : TypographyPolicy.DefaultFamilyName) + " 10"
        },
        ["org.gnome.desktop.wm.preferences"] = new()
        {
            ["button-layout"] = LinuxSystemThemeService.CaptionButtonLayout
        }
    };

    internal static bool IncludesNamespace(IReadOnlyList<string> filters, string ns) =>
        filters.Count == 0 || filters.Any(filter =>
            filter.EndsWith('*') ? ns.StartsWith(filter[..^1], StringComparison.Ordinal) : filter == ns);

    public static Action ReplyFor(MethodContext context)
    {
        var body = context.Request.GetBodyReader();
        var settings = Values();
        if (context.Request.MemberAsString == "Read" && context.Request.SignatureAsString == "ss")
        {
            var ns = body.ReadString(); var key = body.ReadString();
            return () =>
            {
                if (!settings.TryGetValue(ns, out var properties) || !properties.TryGetValue(key, out var value))
                { context.ReplyError("org.freedesktop.portal.Error.NotFound", "Unknown setting."); return; }
                using var writer = context.CreateReplyWriter("v"); writer.WriteVariant(value); context.Reply(writer.CreateMessage());
            };
        }
        if (context.Request.MemberAsString == "ReadAll" && context.Request.SignatureAsString == "as")
        {
            var namespaces = body.ReadArrayOfString();
            return () =>
            {
                using var writer = context.CreateReplyWriter("a{sa{sv}}");
                var dictionary = writer.WriteDictionaryStart();
                foreach (var (ns, properties) in settings)
                {
                    if (!IncludesNamespace(namespaces, ns)) continue;
                    writer.WriteDictionaryEntryStart(); writer.WriteString(ns); writer.WriteDictionary(properties);
                }
                writer.WriteDictionaryEnd(dictionary); context.Reply(writer.CreateMessage());
            };
        }
        return () => context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", "Unknown settings method.");
    }
}
