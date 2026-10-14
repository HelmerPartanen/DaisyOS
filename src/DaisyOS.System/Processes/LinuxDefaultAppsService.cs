using System;
using DaisyOS.Core.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DaisyOS.System.Processes;

public sealed record AppAssociationInfo(string DesktopId, string DisplayName, string? IconName);

public sealed record DefaultAppsStatus(
    IReadOnlyList<AppAssociationInfo> AvailableBrowsers,
    string CurrentBrowserDesktopId,
    IReadOnlyList<AppAssociationInfo> AvailableFileManagers,
    string CurrentFileManagerDesktopId,
    IReadOnlyList<AppAssociationInfo> AvailableTextEditors,
    string CurrentTextEditorDesktopId,
    IReadOnlyList<AppAssociationInfo> AvailableTerminals,
    string CurrentTerminalDesktopId);

public sealed class LinuxDefaultAppsService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);
    private readonly ICommandRunner _commandRunner;
    private readonly IReadOnlyList<string> _desktopDirs;

    public LinuxDefaultAppsService(ICommandRunner? commandRunner = null, IReadOnlyList<string>? desktopDirs = null)
    {
        _commandRunner = commandRunner ?? new SafeCommandRunner();
        _desktopDirs = desktopDirs ??
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications"),
            "/usr/local/share/applications",
            "/usr/share/applications"
        ];
    }

    public async Task<DefaultAppsStatus> GetDefaultAppsAsync(CancellationToken cancellationToken = default)
    {
        var allApps = ScanDesktopFiles();

        // 1. Web Browsers
        var browserCandidates = allApps.Where(a =>
            a.MimeTypes.Contains("x-scheme-handler/http") ||
            a.MimeTypes.Contains("x-scheme-handler/https") ||
            a.Categories.Contains("WebBrowser")).ToList();
        var currentBrowser = await QueryDefaultMimeAsync("x-scheme-handler/https", cancellationToken);

        // 2. File Managers
        var fileManagerCandidates = allApps.Where(a =>
            a.MimeTypes.Contains("inode/directory") ||
            a.Categories.Contains("FileManager")).ToList();
        var currentFileManager = await QueryDefaultMimeAsync("inode/directory", cancellationToken);

        // 3. Text Editors
        var editorCandidates = allApps.Where(a =>
            a.MimeTypes.Contains("text/plain") ||
            a.Categories.Contains("TextEditor")).ToList();
        var currentEditor = await QueryDefaultMimeAsync("text/plain", cancellationToken);

        // 4. Terminals
        var terminalCandidates = allApps.Where(a =>
            a.Categories.Contains("TerminalEmulator")).ToList();

        // If none found by category, use well known terminals
        var knownTerminals = new[] { "org.gnome.Console.desktop", "ptyxis.desktop", "org.kde.konsole.desktop", "alacritty.desktop", "kitty.desktop", "xterm.desktop" };
        foreach (var kt in knownTerminals)
        {
            if (!terminalCandidates.Any(t => string.Equals(t.DesktopId, kt, StringComparison.OrdinalIgnoreCase)))
            {
                var found = allApps.FirstOrDefault(a => string.Equals(a.DesktopId, kt, StringComparison.OrdinalIgnoreCase));
                if (found != null) terminalCandidates.Add(found);
            }
        }

        return new DefaultAppsStatus(
            browserCandidates.Select(b => new AppAssociationInfo(b.DesktopId, b.Name, b.Icon)).ToList(),
            currentBrowser,
            fileManagerCandidates.Select(f => new AppAssociationInfo(f.DesktopId, f.Name, f.Icon)).ToList(),
            currentFileManager,
            editorCandidates.Select(e => new AppAssociationInfo(e.DesktopId, e.Name, e.Icon)).ToList(),
            currentEditor,
            terminalCandidates.Select(t => new AppAssociationInfo(t.DesktopId, t.Name, t.Icon)).ToList(),
            terminalCandidates.FirstOrDefault()?.DesktopId ?? "");
    }

    public async Task<bool> SetDefaultBrowserAsync(string desktopId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(desktopId)) return false;
        var r1 = await SetDefaultMimeAsync(desktopId, "x-scheme-handler/http", cancellationToken);
        var r2 = await SetDefaultMimeAsync(desktopId, "x-scheme-handler/https", cancellationToken);
        var r3 = await SetDefaultMimeAsync(desktopId, "text/html", cancellationToken);
        return r1 || r2 || r3;
    }

    public Task<bool> SetDefaultFileManagerAsync(string desktopId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(desktopId)) return Task.FromResult(false);
        return SetDefaultMimeAsync(desktopId, "inode/directory", cancellationToken);
    }

    public Task<bool> SetDefaultTextEditorAsync(string desktopId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(desktopId)) return Task.FromResult(false);
        return SetDefaultMimeAsync(desktopId, "text/plain", cancellationToken);
    }

    private async Task<string> QueryDefaultMimeAsync(string mimeType, CancellationToken cancellationToken)
    {
        try
        {
            var res = await _commandRunner.RunAsync("xdg-mime", ["query", "default", mimeType], CommandTimeout, cancellationToken);
            if (res.Succeeded && !string.IsNullOrWhiteSpace(res.StandardOutput))
            {
                return res.StandardOutput.Trim().Split('\n')[0].Trim();
            }
        }
        catch { }
        return string.Empty;
    }

    private async Task<bool> SetDefaultMimeAsync(string desktopId, string mimeType, CancellationToken cancellationToken)
    {
        try
        {
            var res = await _commandRunner.RunAsync("xdg-mime", ["default", desktopId, mimeType], CommandTimeout, cancellationToken);
            if (res.Succeeded) return true;
        }
        catch { }

        // Direct fallback: ~/.config/mimeapps.list
        try
        {
            var mimeappsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "mimeapps.list");
            var lines = File.Exists(mimeappsPath) ? (await File.ReadAllLinesAsync(mimeappsPath, cancellationToken)).ToList() : new List<string>();

            var header = "[Default Applications]";
            var headerIdx = lines.FindIndex(l => l.Trim().Equals(header, StringComparison.OrdinalIgnoreCase));
            if (headerIdx < 0)
            {
                lines.Add(header);
                lines.Add($"{mimeType}={desktopId}");
            }
            else
            {
                var entryIdx = lines.FindIndex(headerIdx + 1, l => l.Trim().StartsWith($"{mimeType}=", StringComparison.OrdinalIgnoreCase));
                if (entryIdx >= 0)
                {
                    lines[entryIdx] = $"{mimeType}={desktopId}";
                }
                else
                {
                    lines.Insert(headerIdx + 1, $"{mimeType}={desktopId}");
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(mimeappsPath)!);
            await File.WriteAllLinesAsync(mimeappsPath, lines, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private List<ParsedDesktopEntry> ScanDesktopFiles()
    {
        var entries = new Dictionary<string, ParsedDesktopEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in _desktopDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.desktop", SearchOption.TopDirectoryOnly))
                {
                    var id = Path.GetFileName(file);
                    if (entries.ContainsKey(id)) continue;

                    var parsed = ParseDesktopFile(file, id);
                    if (parsed != null && !parsed.NoDisplay)
                    {
                        entries[id] = parsed;
                    }
                }
            }
            catch { }
        }
        return entries.Values.ToList();
    }

    private static ParsedDesktopEntry? ParseDesktopFile(string path, string desktopId)
    {
        try
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            string name = "";
            string icon = "";
            bool noDisplay = false;
            var mimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool inDesktopEntry = false;
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inDesktopEntry = string.Equals(line, "[Desktop Entry]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inDesktopEntry || line.StartsWith('#')) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();

                if (key == "Name" || key.StartsWith("Name[", StringComparison.Ordinal))
                {
                    names[key] = val;
                }
                else if (string.Equals(key, "Icon", StringComparison.OrdinalIgnoreCase))
                {
                    icon = val;
                }
                else if (string.Equals(key, "NoDisplay", StringComparison.OrdinalIgnoreCase))
                {
                    noDisplay = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                }
                else if (string.Equals(key, "MimeType", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var m in val.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        mimeTypes.Add(m);
                    }
                }
                else if (string.Equals(key, "Categories", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var c in val.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        categories.Add(c);
                    }
                }
            }

            name = DesktopEntryLocalization.GetValue(names, "Name") ?? Path.GetFileNameWithoutExtension(desktopId);
            return new ParsedDesktopEntry(desktopId, name, icon, noDisplay, mimeTypes, categories);
        }
        catch
        {
            return null;
        }
    }

    private sealed record ParsedDesktopEntry(
        string DesktopId,
        string Name,
        string Icon,
        bool NoDisplay,
        HashSet<string> MimeTypes,
        HashSet<string> Categories);
}

