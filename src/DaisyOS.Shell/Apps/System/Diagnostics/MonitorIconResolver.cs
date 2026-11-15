using Avalonia.Media;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

/// <summary>Resolves each application/executable once on a worker, including icons for app-owned helpers.</summary>
public sealed class MonitorIconResolver(Func<IEnumerable<string?>, IImage?>? load = null)
{
    private readonly Func<IEnumerable<string?>, IImage?> _load = load ?? LoadSpecificIcon;
    private readonly Dictionary<string, IImage?> _cache = new();
    public Task<IReadOnlyDictionary<int, IImage?>> ResolveAsync(IReadOnlyList<ProcessItemInfo> processes, IReadOnlyList<CompositorWindowRecord> windows, CancellationToken token)
    {
        var theme = DesktopItemLoader.IsCurrentThemeLight() ? "light:" : "dark:";
        return Task.Run<IReadOnlyDictionary<int, IImage?>>(() =>
    {
        var result = new Dictionary<int, IImage?>();
        var groups = ApplicationAttribution.Group(windows, processes);
        var active = new HashSet<string>();
        IImage? Resolve(string key, IEnumerable<string?> identities)
        {
            key = theme + key;
            active.Add(key);
            if (!_cache.TryGetValue(key, out var image)) _cache[key] = image = _load(identities);
            return image;
        }
        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            var w = group.Windows[0];
            var image = Resolve("app:" + group.Key, [w.IconName, w.DesktopFileId, w.DaisyAppId, w.AppId]);
            if (image is not null) foreach (var p in group.Processes) result[p.Pid] = image;
        }
        foreach (var p in processes)
        {
            token.ThrowIfCancellationRequested();
            if (result.ContainsKey(p.Pid)) continue;
            result[p.Pid] = Resolve("exe:" + p.ExecutablePath + "|" + p.Name, [p.Name, Path.GetFileNameWithoutExtension(p.ExecutablePath), p.ExecutablePath]);
        }
        foreach (var key in _cache.Keys.Where(k => !active.Contains(k)).ToArray()) _cache.Remove(key);
        return result;
    }, token);
    }
    private static IImage? LoadSpecificIcon(IEnumerable<string?> identities)
    {
        var candidates = identities.OfType<string>().Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        foreach (var candidate in candidates)
        {
            var executable = Path.GetFileName(candidate);
            if (!executable.StartsWith("DaisyOS.", StringComparison.OrdinalIgnoreCase)) continue;
            var native = DesktopItemLoader.GetAppIconPath(executable[8..], DesktopItemLoader.IsCurrentThemeLight());
            if (!string.IsNullOrWhiteSpace(native)) return DesktopItemLoader.LoadBitmapSafe(native);
        }
        // A shared runtime (for example Electron) must not override a more specific process name.
        return DesktopItemLoader.LoadApplicationIcon(candidates.Take(1)) ?? DesktopItemLoader.LoadApplicationIcon(candidates.Skip(1));
    }
}
