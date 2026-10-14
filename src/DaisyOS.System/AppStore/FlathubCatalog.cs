using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using DaisyOS.Core.Models;

namespace DaisyOS.System.AppStore;

/// <summary>Streams Flathub's AppStream metadata; never constructs a whole-catalog DOM.</summary>
internal static class FlathubCatalog
{
    internal static readonly string[] Picks = ["org.mozilla.firefox", "org.libreoffice.LibreOffice", "org.gimp.GIMP",
        "org.videolan.VLC", "org.blender.Blender", "md.obsidian.Obsidian", "com.valvesoftware.Steam",
        "org.kde.krita", "com.obsproject.Studio", "org.gnome.Builder", "org.gnome.World.PikaBackup", "org.localsend.localsend_app"];

    internal static IReadOnlyList<AppStoreItem> Read(Stream stream, string locale, string? iconDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var apps = new Dictionary<string, AppStoreItem>(StringComparer.Ordinal);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 120_000_000, IgnoreWhitespace = true });
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "component") continue;
            using var subtree = reader.ReadSubtree();
            var c = XElement.Load(subtree);
            if ((string?)c.Attribute("type") is not ("desktop-application" or "desktop")) continue;
            var bundle = c.Elements("bundle").FirstOrDefault(e => (string?)e.Attribute("type") == "flatpak")?.Value;
            var id = bundle?.Split('/').ElementAtOrDefault(1) ?? c.Element("id")?.Value ?? "";
            if (!LinuxAppStoreService.ValidAppId(id)) continue;
            var name = Text(c.Elements("name"), locale);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var categories = c.Element("categories")?.Elements("category").Select(e => e.Value).ToArray() ?? [];
            var category = Category(categories);
            var remoteIcon = c.Elements("icon").Where(e => (string?)e.Attribute("type") == "remote")
                .OrderByDescending(e => (int?)e.Attribute("width") ?? 0).Select(e => e.Value).FirstOrDefault(IsMediaUrl) ?? "";
            var cachedIcon = c.Elements("icon").FirstOrDefault(e => (string?)e.Attribute("type") == "cached"
                && (string?)e.Attribute("width") == "128" && e.Attribute("scale") == null)?.Value;
            if (iconDirectory != null && cachedIcon != null && Path.GetFileName(cachedIcon) == cachedIcon)
            {
                var local = Path.Combine(iconDirectory, "128x128", cachedIcon);
                if (File.Exists(local)) remoteIcon = local;
            }
            var release = c.Element("releases")?.Elements("release").FirstOrDefault();
            var verified = c.Element("custom")?.Elements("value").Any(e =>
                (string?)e.Attribute("key") == "flathub::verification::verified" && e.Value == "true") == true;
            apps[id] = new AppStoreItem(id, id, name, Text(c.Elements("summary"), locale),
                PlainText(Localized(c.Elements("description"), locale)), category, remoteIcon, Glyph(category),
                (string?)release?.Attribute("version") ?? "", Text(c.Element("developer")?.Elements("name") ?? c.Elements("developer_name"), locale),
                "", 0, Picks.Contains(id), false)
            {
                DesktopFileId = c.Elements("launchable").FirstOrDefault(e => (string?)e.Attribute("type") == "desktop-id")?.Value ?? id + ".desktop",
                Categories = categories,
                Keywords = Localized(c.Elements("keywords"), locale)?.Elements("keyword").Select(e => e.Value).ToArray() ?? [],
                License = c.Element("project_license")?.Value ?? "",
                Homepage = c.Elements("url").FirstOrDefault(e => (string?)e.Attribute("type") == "homepage")?.Value ?? "",
                IsVerified = verified,
                ReleaseNotes = PlainText(Localized(release?.Elements("description") ?? [], locale)),
                Screenshots = c.Element("screenshots")?.Elements("screenshot").Select(s => s.Elements("image")
                    .Where(i => IsMediaUrl(i.Value)).OrderBy(i => Math.Abs(((int?)i.Attribute("width") ?? 2000) - 752))
                    .Select(i => i.Value).FirstOrDefault()).OfType<string>().Take(8).ToArray() ?? []
            };
        }
        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    private static XElement? Localized(IEnumerable<XElement> elements, string locale)
    {
        var all = elements.ToArray();
        var language = locale.Split('-', '_')[0];
        return all.FirstOrDefault(e => (string?)e.Attribute(XNamespace.Xml + "lang") == locale)
            ?? all.FirstOrDefault(e => (string?)e.Attribute(XNamespace.Xml + "lang") == language)
            ?? all.FirstOrDefault(e => e.Attribute(XNamespace.Xml + "lang") == null) ?? all.FirstOrDefault();
    }
    private static string Text(IEnumerable<XElement> e, string locale) => Localized(e, locale)?.Value.Trim() ?? "";
    private static string PlainText(XElement? e) => e == null ? "" : string.Join("\n\n", e.Elements()
        .Select(p => p.Name.LocalName is "ul" or "ol" ? string.Join("\n", p.Elements("li").Select(li => "• " + li.Value.Trim())) : p.Value.Trim()));
    internal static bool IsMediaUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "dl.flathub.org" && (uri.AbsolutePath.StartsWith("/media/", StringComparison.Ordinal)
            || uri.AbsolutePath.StartsWith("/repo/screenshots/", StringComparison.Ordinal)
            || uri.AbsolutePath.StartsWith("/repo/appstream/", StringComparison.Ordinal));
    internal static AppCategory Category(IReadOnlyList<string> categories) => categories switch
    {
        _ when categories.Contains("Game") => AppCategory.Gaming,
        _ when categories.Contains("Development") => AppCategory.Development,
        _ when categories.Contains("Graphics") => AppCategory.Creative,
        _ when categories.Contains("Audio") => AppCategory.Audio,
        _ when categories.Contains("Video") || categories.Contains("AudioVideo") => AppCategory.Video,
        _ when categories.Contains("Network") => AppCategory.Internet,
        _ when categories.Contains("Education") || categories.Contains("Science") => AppCategory.Education,
        _ when categories.Contains("Office") => AppCategory.Productivity,
        _ => AppCategory.Utilities
    };
    internal static string Glyph(AppCategory category) => category switch
    {
        AppCategory.Gaming => "game-controller", AppCategory.Development => "code", AppCategory.Creative => "paint-brush",
        AppCategory.Audio => "music-note", AppCategory.Video => "film-strip", AppCategory.Internet => "globe",
        AppCategory.Education => "book-open", AppCategory.Productivity => "briefcase", _ => "package"
    };
}
