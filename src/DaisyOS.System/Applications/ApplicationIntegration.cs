using DaisyOS.Core.Models;
using DaisyOS.System.Processes;
namespace DaisyOS.System.Applications;

public sealed class ApplicationIntegration(ICommandRunner runner, string? dataHome = null)
{
    private string DataHome => dataHome ?? Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share");
    public static string IdentityTag(ApplicationIdentity identity) => $"{identity.Backend}:{identity.Scope}:{identity.Container}:{identity.PackageId}:{identity.Reference}";
    public static string[] Commands(IEnumerable<string> files) => files.Where(File.Exists)
        .SelectMany(file => File.ReadLines(file).Where(line => line.StartsWith("Exec=", StringComparison.Ordinal)).Take(1)).Distinct().ToArray();

    public async Task RemoveAsync(InstalledApplication app, CancellationToken token = default)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var desktop = DesktopDirectory();
        foreach (var directory in new[] { desktop, Path.Combine(DataHome, "applications") })
            if (Directory.Exists(directory)) foreach (var path in Directory.EnumerateFiles(directory, "*.desktop")) candidates.Add(path);
        foreach (var path in app.OwnedLaunchers)
            if (ApplicationRegistry.IsWithin(path, DataHome) || ApplicationRegistry.IsWithin(path, desktop)) candidates.Add(path);
        foreach (var path in candidates)
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(path) || ApplicationRegistry.HasSymbolicAncestor(path)) continue;
            var content = File.ReadAllLines(path);
            if (!BelongsTo(app, path, content)) continue;
            if (app.Identity.Backend == ApplicationBackend.Flatpak && !HasExplicitScope(app, content))
            {
                // A generic Flatpak launcher can still represent another installed scope.
                var other = await runner.RunAsync("flatpak", ["list", "--app", "--columns=application,installation"], TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                if (!other.Succeeded || other.StandardOutput.Split('\n').Select(line => line.Split('\t')).Any(parts => parts.Length >= 1 && parts[0] == app.Identity.PackageId)) continue;
            }
            File.Delete(path);
        }
        foreach (var icon in app.OwnedIcons.Where(path => path.Length > 0))
            if (ApplicationRegistry.IsWithin(icon, Path.Combine(DataHome, "icons")) && File.Exists(icon) && !ApplicationRegistry.HasSymbolicAncestor(icon)) File.Delete(icon);
        foreach (var path in app.OwnedIntegrationFiles)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!ApplicationRegistry.IsWithin(path, DataHome) && !ApplicationRegistry.IsWithin(path, Path.Combine(home, ".local/bin"))) continue;
            var info = new FileInfo(path);
            if (info.LinkTarget is { } target)
            {
                var full = Path.GetFullPath(target, Path.GetDirectoryName(path)!);
                if (app.ReviewedPaths.Any(folder => ApplicationRegistry.IsWithin(full, folder))) File.Delete(path);
            }
            else if (File.Exists(path) && !ApplicationRegistry.HasSymbolicAncestor(path) && app.SourcePath.Length > 0 && File.ReadAllText(path).Contains(app.SourcePath, StringComparison.Ordinal)) File.Delete(path);
        }
        await RefreshAsync(token).ConfigureAwait(false);
    }
    public static bool BelongsTo(InstalledApplication app, string path, IReadOnlyList<string> content)
    {
        var tag = content.FirstOrDefault(line => line.StartsWith("X-DaisyOS-Application=", StringComparison.Ordinal));
        if (tag is not null) return tag == "X-DaisyOS-Application=" + IdentityTag(app.Identity);
        if (app.Identity.Backend == ApplicationBackend.Flatpak && content.Contains("X-Flatpak=" + app.Identity.PackageId)) return true;
        var source = content.FirstOrDefault(line => line.StartsWith("X-DaisyOS-SourceDesktopFile=", StringComparison.Ordinal));
        if (source is not null) return app.OwnedLaunchers.Contains(source["X-DaisyOS-SourceDesktopFile=".Length..], StringComparer.Ordinal);
        var command = content.FirstOrDefault(line => line.StartsWith("Exec=", StringComparison.Ordinal));
        // Content identity handles renamed copies without treating a matching filename alone as ownership.
        if (command is not null && app.OwnedCommands.Contains(command, StringComparer.Ordinal)) return true;
        return app.OwnedLaunchers.Contains(path, StringComparer.Ordinal) && app.Identity.Backend is ApplicationBackend.Container or ApplicationBackend.AppImage;
    }
    private static bool HasExplicitScope(InstalledApplication app, IReadOnlyList<string> lines) => lines.Any(line => line == "X-DaisyOS-Application=" + IdentityTag(app.Identity) ||
        line.StartsWith("Exec=", StringComparison.Ordinal) && (line.Contains("--" + app.Identity.Scope + " ", StringComparison.Ordinal) || line.Contains("--installation=" + app.Identity.Scope + " ", StringComparison.Ordinal)));
    public async Task RefreshAsync(CancellationToken token = default)
    {
        foreach (var (command, args) in new (string, string[])[] { ("update-desktop-database", [Path.Combine(DataHome, "applications")]), ("update-mime-database", [Path.Combine(DataHome, "mime")]) })
            if (Directory.Exists(args[0])) await runner.RunAsync(command, args, TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
    }
    private static string DesktopDirectory()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        return string.IsNullOrWhiteSpace(desktop) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop") : desktop;
    }
}
