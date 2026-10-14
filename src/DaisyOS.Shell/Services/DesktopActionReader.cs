using System;
using System.Globalization;
using DaisyOS.Core.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DaisyOS.Shell.Services;

public sealed record DesktopActionInfo(string Id, string Name, string Exec, string? Icon = null);

public static class DesktopActionReader
{
    private static readonly Dictionary<string, string> BuiltInAppDesktopFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calendar"] = "/usr/share/applications/daisyos-calendar.desktop",
        ["notes"] = "/usr/share/applications/daisyos-notes.desktop",
        ["calculator"] = "/usr/share/applications/daisyos-calculator.desktop",
        ["settings"] = "/usr/share/applications/daisyos-settings.desktop",
        ["files"] = "/usr/share/applications/daisyos-files.desktop",
        ["uikit"] = "/usr/share/applications/org.daisyos.uikit.desktop"
    };

    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, string?> _resolvedDesktopPathCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<DesktopActionInfo>> _actionCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<string[]> _cachedDesktopFiles = new(() => EnumerateAllDesktopFiles().ToArray());

    public static IReadOnlyList<DesktopActionInfo> GetActions(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Array.Empty<DesktopActionInfo>();
        }

        return _actionCache.GetOrAdd(identifier + "\x1f" + CultureInfo.CurrentUICulture.Name, _ =>
        {
            var desktopPath = ResolveDesktopFilePath(identifier);
            if (string.IsNullOrWhiteSpace(desktopPath) || !File.Exists(desktopPath))
            {
                return Array.Empty<DesktopActionInfo>();
            }

            return ReadActionsFromFile(desktopPath);
        });
    }

    public static string? ResolveDesktopFilePath(string identifier)
    {
        var cleanId = identifier.Trim();
        if (cleanId.StartsWith("kwin:", StringComparison.OrdinalIgnoreCase))
        {
            cleanId = cleanId["kwin:".Length..];
        }

        return _resolvedDesktopPathCache.GetOrAdd(cleanId, ResolveDesktopFilePathUncached);
    }

    private static string? ResolveDesktopFilePathUncached(string cleanId)
    {
        if (File.Exists(cleanId) && cleanId.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
        {
            return cleanId;
        }

        if (BuiltInAppDesktopFiles.TryGetValue(cleanId, out var builtInPath) && File.Exists(builtInPath))
        {
            return builtInPath;
        }

        var allDesktopFiles = _cachedDesktopFiles.Value;

        // 1. Direct filename match (e.g. "google-chrome.desktop" or "google-chrome" -> "google-chrome.desktop")
        var match = allDesktopFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f), cleanId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f), cleanId + ".desktop", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(f), cleanId, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            return match;
        }

        // 2. StartupWMClass, Exec, or X-Flatpak matching
        foreach (var file in allDesktopFiles)
        {
            try
            {
                var entry = ReadDesktopEntryHeader(file);
                if (entry.TryGetValue("StartupWMClass", out var wmClass) &&
                    string.Equals(wmClass, cleanId, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }

                if (entry.TryGetValue("X-Flatpak", out var flatpakId) &&
                    string.Equals(flatpakId, cleanId, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }

                if (entry.TryGetValue("Exec", out var exec))
                {
                    var exe = Path.GetFileName(exec.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty);
                    if (string.Equals(exe, cleanId, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
            }
            catch
            {
                // Ignore IO issues on individual files
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateAllDesktopFiles()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>
        {
            Path.Combine(home, ".local", "share", "applications"),
            Path.Combine(home, ".local", "share", "flatpak", "exports", "share", "applications"),
            "/var/lib/flatpak/exports/share/applications",
            "/var/lib/snapd/desktop/applications",
            "/usr/local/share/applications",
            "/usr/share/applications"
        };

        var configuredRoots = Environment.GetEnvironmentVariable("XDG_DATA_DIRS")
            ?.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        foreach (var dir in configuredRoots)
        {
            roots.Add(Path.Combine(dir, "applications"));
        }

        return roots.Distinct(StringComparer.Ordinal)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.desktop", SearchOption.TopDirectoryOnly));
    }

    private static Dictionary<string, string> ReadDesktopEntryHeader(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inDesktopEntry = false;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inDesktopEntry = string.Equals(line, "[Desktop Entry]", StringComparison.OrdinalIgnoreCase);
                if (!inDesktopEntry && result.Count > 0)
                {
                    break;
                }
                continue;
            }

            if (!inDesktopEntry) continue;

            var sep = line.IndexOf('=');
            if (sep <= 0) continue;
            var key = line[..sep].Trim();
            var val = line[(sep + 1)..].Trim();
            result[key] = val;
        }

        return result;
    }

    private static IReadOnlyList<DesktopActionInfo> ReadActionsFromFile(string path)
    {
        var actions = new List<DesktopActionInfo>();
        try
        {
            var lines = File.ReadAllLines(path);
            var actionIds = new List<string>();
            var inDesktopEntry = false;
            string? currentActionId = null;
            var currentActionDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var actionSections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    if (currentActionId != null && currentActionDict.Count > 0)
                    {
                        actionSections[currentActionId] = new Dictionary<string, string>(currentActionDict, StringComparer.OrdinalIgnoreCase);
                        currentActionDict.Clear();
                    }

                    var section = line[1..^1];
                    if (string.Equals(section, "Desktop Entry", StringComparison.OrdinalIgnoreCase))
                    {
                        inDesktopEntry = true;
                        currentActionId = null;
                    }
                    else if (section.StartsWith("Desktop Action ", StringComparison.OrdinalIgnoreCase))
                    {
                        inDesktopEntry = false;
                        currentActionId = section["Desktop Action ".Length..].Trim();
                    }
                    else
                    {
                        inDesktopEntry = false;
                        currentActionId = null;
                    }
                    continue;
                }

                var sep = line.IndexOf('=');
                if (sep <= 0) continue;
                var key = line[..sep].Trim();
                var val = line[(sep + 1)..].Trim();

                if (inDesktopEntry)
                {
                    if (string.Equals(key, "Actions", StringComparison.OrdinalIgnoreCase))
                    {
                        var ids = val.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        actionIds.AddRange(ids);
                    }
                }
                else if (currentActionId != null)
                {
                    if (string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase) || !currentActionDict.ContainsKey(key))
                    {
                        currentActionDict[key] = val;
                    }
                }
            }

            if (currentActionId != null && currentActionDict.Count > 0)
            {
                actionSections[currentActionId] = new Dictionary<string, string>(currentActionDict, StringComparer.OrdinalIgnoreCase);
            }

            if (actionIds.Count > 0)
            {
                foreach (var id in actionIds)
                {
                    if (actionSections.TryGetValue(id, out var dict))
                    {
                        if (dict.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name)
                            && dict.TryGetValue("Exec", out var exec) && !string.IsNullOrWhiteSpace(exec))
                        {
                            dict.TryGetValue("Icon", out var icon);
                            actions.Add(new DesktopActionInfo(id, DesktopEntryLocalization.GetValue(dict, "Name") ?? name, exec, icon));
                        }
                    }
                }
            }
            else
            {
                foreach (var (id, dict) in actionSections)
                {
                    if (dict.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name)
                        && dict.TryGetValue("Exec", out var exec) && !string.IsNullOrWhiteSpace(exec))
                    {
                        dict.TryGetValue("Icon", out var icon);
                        actions.Add(new DesktopActionInfo(id, DesktopEntryLocalization.GetValue(dict, "Name") ?? name, exec, icon));
                    }
                }
            }
        }
        catch
        {
            // Return whatever was successfully collected
        }

        return actions;
    }
}
