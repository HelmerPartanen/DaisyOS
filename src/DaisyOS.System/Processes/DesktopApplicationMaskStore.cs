using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DaisyOS.Core;

namespace DaisyOS.System.Processes;

/// <summary>
/// Manages user-level application removal and XDG desktop entry masking so that
/// every application (including built-in DaisyOS apps, standalone .desktop apps,
/// and user-installed shortcuts) can be cleanly uninstalled and restored.
/// </summary>
public static class DesktopApplicationMaskStore
{
    private static long _version;

    public static long Version => Interlocked.Read(ref _version);

    private static readonly Dictionary<string, string> ShortDaisyAppIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calculator"] = "org.daisyos.calculator.desktop",
        ["calendar"] = "org.daisyos.calendar.desktop",
        ["notes"] = "org.daisyos.notes.desktop",
        ["files"] = "org.daisyos.files.desktop",
        ["settings"] = "org.daisyos.settings.desktop",
        ["appstore"] = "org.daisyos.appstore.desktop",
        ["diskmanager"] = "org.daisyos.diskmanager.desktop",
        ["taskmanager"] = "org.daisyos.taskmanager.desktop",
        ["systemmonitor"] = "org.daisyos.taskmanager.desktop",
        ["system-monitor"] = "org.daisyos.taskmanager.desktop",
        ["diagnostics"] = "org.daisyos.taskmanager.desktop",
        ["uikit"] = "org.daisyos.uikit.desktop",
        ["ui-kit"] = "org.daisyos.uikit.desktop",
        ["terminal"] = "org.daisyos.terminal.desktop",
        ["installer"] = "org.daisyos.installer.desktop",
    };

    public static string UserApplicationsDirectory
    {
        get
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                dataHome = Path.Combine(home, ".local", "share");
            }
            return Path.Combine(dataHome, "applications");
        }
    }

    public static string NormalizeDesktopId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        var trimmed = Path.GetFileName(id.Trim());
        if (ShortDaisyAppIds.TryGetValue(trimmed, out var mapped))
            return mapped;

        var withoutDesktop = trimmed.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^8]
            : trimmed;
        if (ShortDaisyAppIds.TryGetValue(withoutDesktop, out mapped))
            return mapped;

        return trimmed.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".desktop";
    }

    public static string NormalizeShortId(string id)
    {
        var desktopId = NormalizeDesktopId(id);
        foreach (var pair in ShortDaisyAppIds)
            if (string.Equals(pair.Value, desktopId, StringComparison.OrdinalIgnoreCase))
                return pair.Key;
        return desktopId.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
            ? desktopId[..^8] : desktopId;
    }

    public static IReadOnlySet<string> GetMaskedIds()
    {
        var masked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dir = UserApplicationsDirectory;
        if (!Directory.Exists(dir)) return masked;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.desktop", SearchOption.TopDirectoryOnly))
            {
                if (!IsDesktopFileHiddenOrMasked(file)) continue;
                AddIdVariants(masked, Path.GetFileName(file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return masked;
    }

    public static bool IsMasked(string id)
    {
        var normalized = NormalizeDesktopId(id);
        if (string.IsNullOrEmpty(normalized)) return false;
        var maskPath = Path.Combine(UserApplicationsDirectory, normalized);
        return File.Exists(maskPath) && IsDesktopFileHiddenOrMasked(maskPath);
    }

    public static bool IsDesktopFileHiddenOrMasked(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            var inDesktopEntry = false;
            foreach (var rawLine in File.ReadLines(filePath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inDesktopEntry = string.Equals(line, "[Desktop Entry]", StringComparison.Ordinal);
                    continue;
                }
                if (!inDesktopEntry) continue;
                var sep = line.IndexOf('=');
                if (sep <= 0) continue;
                var key = line[..sep].Trim();
                var val = line[(sep + 1)..].Trim();
                if ((string.Equals(key, "Hidden", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(key, "X-DaisyOS-Removed", StringComparison.OrdinalIgnoreCase)) &&
                    string.Equals(val, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return false;
    }

    public static bool CommandExists(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var parsed = DesktopExecParser.Parse(command);
        var executable = parsed is { Count: > 0 } ? parsed[0] : command.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(executable)) return false;

        if (Path.IsPathFullyQualified(executable))
            return File.Exists(executable);

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
        foreach (var dir in pathEnv.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, executable);
                if (File.Exists(candidate)) return true;
            }
            catch
            {
            }
        }
        return false;
    }

    public static bool RemoveOrMask(string id, string? desktopEntryPath = null, string? displayName = null, string? icon = null)
    {
        var desktopId = NormalizeDesktopId(id);
        if (string.IsNullOrEmpty(desktopId)) return false;

        try
        {
            var userDir = UserApplicationsDirectory;
            Directory.CreateDirectory(userDir);
            var userFile = Path.Combine(userDir, desktopId);

            var hasSystemEntry = HasSystemOrBuiltInEntry(desktopId);
            if (!hasSystemEntry && !string.IsNullOrWhiteSpace(desktopEntryPath))
            {
                var fullSource = Path.GetFullPath(desktopEntryPath);
                var fullUserDir = Path.GetFullPath(userDir);
                if (fullSource.StartsWith(fullUserDir + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(fullSource))
                {
                    File.Delete(fullSource);
                    RemoveDesktopShortcut(desktopId);
                    Interlocked.Increment(ref _version);
                    return true;
                }
            }

            var resolvedName = !string.IsNullOrWhiteSpace(displayName)
                ? displayName.Replace('\n', ' ').Replace('\r', ' ').Trim()
                : desktopId.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? desktopId[..^8] : desktopId;
            var resolvedIcon = !string.IsNullOrWhiteSpace(icon)
                ? icon.Replace('\n', ' ').Replace('\r', ' ').Trim()
                : "application-x-executable";

            var content = string.Join('\n',
                "[Desktop Entry]",
                "Type=Application",
                $"Name={resolvedName}",
                $"Icon={resolvedIcon}",
                "Exec=false",
                "Hidden=true",
                "NoDisplay=true",
                "X-DaisyOS-Removed=true",
                "");

            AtomicTextFile.Write(userFile, content);
            RemoveDesktopShortcut(desktopId);
            Interlocked.Increment(ref _version);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool Restore(string id)
    {
        var desktopId = NormalizeDesktopId(id);
        if (string.IsNullOrEmpty(desktopId)) return false;

        try
        {
            var userFile = Path.Combine(UserApplicationsDirectory, desktopId);
            if (File.Exists(userFile) && IsDesktopFileHiddenOrMasked(userFile))
            {
                File.Delete(userFile);
                Interlocked.Increment(ref _version);
                return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static IReadOnlyList<(string DesktopId, string Name, string Icon)> GetRemovedApplications()
    {
        var results = new List<(string DesktopId, string Name, string Icon)>();
        var dir = UserApplicationsDirectory;
        if (!Directory.Exists(dir)) return results;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.desktop", SearchOption.TopDirectoryOnly))
            {
                var isRemoved = false;
                var name = string.Empty;
                var icon = "application-x-executable";
                var inDesktopEntry = false;

                foreach (var rawLine in File.ReadLines(file))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        inDesktopEntry = string.Equals(line, "[Desktop Entry]", StringComparison.Ordinal);
                        continue;
                    }
                    if (!inDesktopEntry) continue;
                    var sep = line.IndexOf('=');
                    if (sep <= 0) continue;
                    var key = line[..sep].Trim();
                    var val = line[(sep + 1)..].Trim();
                    if (string.Equals(key, "X-DaisyOS-Removed", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(val, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        isRemoved = true;
                    }
                    else if (string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(name))
                    {
                        name = val;
                    }
                    else if (string.Equals(key, "Icon", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val))
                    {
                        icon = val;
                    }
                }

                if (isRemoved)
                {
                    var desktopId = Path.GetFileName(file);
                    if (string.IsNullOrWhiteSpace(name))
                        name = desktopId.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? desktopId[..^8] : desktopId;
                    results.Add((desktopId, name, icon));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return results.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static void RemoveDesktopShortcut(string id)
    {
        try
        {
            var desktopId = NormalizeDesktopId(id);
            if (string.IsNullOrEmpty(desktopId)) return;
            var desktopDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            if (!Directory.Exists(desktopDir)) return;

            var candidate = Path.Combine(desktopDir, desktopId);
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool HasSystemOrBuiltInEntry(string desktopId)
    {
        if (desktopId.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase))
            return true;

        var systemDirs = new[]
        {
            "/usr/share/applications",
            "/usr/local/share/applications",
            "/var/lib/flatpak/exports/share/applications",
            "/var/lib/snapd/desktop/applications"
        };

        foreach (var dir in systemDirs)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, desktopId))) return true;
            }
            catch
            {
            }
        }
        return false;
    }

    private static void AddIdVariants(HashSet<string> set, string desktopId)
    {
        set.Add(desktopId);
        var withoutExt = desktopId.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
            ? desktopId[..^8]
            : desktopId;
        set.Add(withoutExt);
        set.Add(withoutExt + ".desktop");
        if (withoutExt.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase))
        {
            var shortName = withoutExt["org.daisyos.".Length..];
            set.Add(shortName);
            set.Add(shortName + ".desktop");
        }
    }
}
