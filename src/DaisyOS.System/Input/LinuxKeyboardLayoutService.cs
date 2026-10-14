using System.Text.RegularExpressions;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Input;

/// <summary>
/// Keeps the system default and the active KWin keyboard map aligned. The
/// system setting persists across sign-in; KWin's map is applied and verified
/// separately so Settings never reports a layout that is not actually typing.
/// </summary>
public sealed partial class LinuxKeyboardLayoutService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ChangeTimeout = TimeSpan.FromSeconds(20);
    private readonly ICommandRunner _commandRunner;
    private readonly string _xkbRulesPath;

    public LinuxKeyboardLayoutService(ICommandRunner commandRunner, string? xkbRulesPath = null)
    {
        _commandRunner = commandRunner;
        _xkbRulesPath = xkbRulesPath ?? "/usr/share/X11/xkb/rules/evdev.lst";
    }

    public async Task<KeyboardLayoutStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var statusTask = _commandRunner.RunAsync("localectl", ["status"], QueryTimeout, cancellationToken);
        var layoutsTask = _commandRunner.RunAsync("localectl", ["list-x11-keymap-layouts", "--no-pager"], QueryTimeout, cancellationToken);
        var activeLayoutTask = _commandRunner.RunAsync(
            "qdbus6",
            ["--literal", "org.kde.keyboard", "/Layouts", "org.kde.KeyboardLayouts.getLayoutsList"],
            QueryTimeout,
            cancellationToken);
        await Task.WhenAll(statusTask, layoutsTask, activeLayoutTask);

        var status = await statusTask;
        var layouts = await layoutsTask;
        if (!status.Succeeded || !layouts.Succeeded)
        {
            return new KeyboardLayoutStatus(
                false,
                "",
                "",
                [],
                "Keyboard layouts are unavailable because the system keyboard service could not be reached.");
        }

        var names = ReadLayoutNames();
        var availableLayouts = layouts.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsSafeLayoutId)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new KeyboardLayoutOption(id, names.GetValueOrDefault(id, id)))
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var configured = ParseCurrentLayout(status.StandardOutput);
        var activeLayoutResult = await activeLayoutTask;
        var active = activeLayoutResult.Succeeded
            ? ParseKwinLayout(activeLayoutResult.StandardOutput)
            : string.Empty;
        var current = string.IsNullOrWhiteSpace(active) ? configured : active;
        var detail = availableLayouts.Length == 0
            ? "No keyboard layouts were reported by the system."
            : !string.IsNullOrWhiteSpace(active) &&
              !string.Equals(active, configured, StringComparison.OrdinalIgnoreCase)
                ? $"The saved system layout is {configured}; the active session is {active}. Choose the saved layout and apply it to correct this session."
                : "Keyboard layout is provided by the active Linux session.";
        return new KeyboardLayoutStatus(
            true,
            current,
            configured,
            availableLayouts,
            detail);
    }

    public async Task<KeyboardLayoutResult> SetLayoutAsync(string layoutId, CancellationToken cancellationToken = default)
    {
        if (!IsSafeLayoutId(layoutId))
        {
            return new KeyboardLayoutResult(false, "Choose a valid keyboard layout.");
        }

        // First persist the choice for the display manager, virtual consoles,
        // and XWayland. This can show the normal polkit prompt.
        var systemDefault = await _commandRunner.RunAsync(
            "localectl",
            ["set-x11-keymap", layoutId],
            ChangeTimeout,
            cancellationToken);

        if (!systemDefault.Succeeded)
        {
            return new KeyboardLayoutResult(
                false,
                "Couldn’t change the keyboard layout. Authenticate if prompted, then try again.");
        }

        // KWin uses this config for its live keyboard service. It is an
        // implementation detail, not a user-selectable second layout source.
        var layoutConfig = await _commandRunner.RunAsync(
            "kwriteconfig6",
            ["--file", "kxkbrc", "--group", "Layout", "--key", "LayoutList", layoutId, "--notify"],
            ChangeTimeout,
            cancellationToken);
        var enableConfig = await _commandRunner.RunAsync(
            "kwriteconfig6",
            ["--file", "kxkbrc", "--group", "Layout", "--key", "Use", "true", "--type", "bool", "--notify"],
            ChangeTimeout,
            cancellationToken);
        var reconfigure = await _commandRunner.RunAsync(
            "qdbus6",
            ["org.kde.KWin", "/KWin", "org.kde.KWin.reconfigure"],
            ChangeTimeout,
            cancellationToken);
        var activeLayout = await _commandRunner.RunAsync(
            "qdbus6",
            ["--literal", "org.kde.keyboard", "/Layouts", "org.kde.KeyboardLayouts.getLayoutsList"],
            ChangeTimeout,
            cancellationToken);
        var applied = layoutConfig.Succeeded &&
                      enableConfig.Succeeded &&
                      reconfigure.Succeeded &&
                      activeLayout.Succeeded &&
                      string.Equals(ParseKwinLayout(activeLayout.StandardOutput), layoutId, StringComparison.OrdinalIgnoreCase);

        return applied
            ? new KeyboardLayoutResult(true, "Keyboard layout applied.", true)
            : new KeyboardLayoutResult(true, "Keyboard layout was saved. Sign out and back in if it is not active yet.", false);
    }

    internal static string ParseCurrentLayout(string output)
    {
        var layout = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(':', 2))
            .FirstOrDefault(parts => parts.Length == 2 && string.Equals(parts[0].Trim(), "X11 Layout", StringComparison.OrdinalIgnoreCase));
        var value = layout is { Length: 2 } ? layout[1].Trim() : string.Empty;
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
    }

    internal static bool IsSafeLayoutId(string value) =>
        !string.IsNullOrWhiteSpace(value) && LayoutIdRegex().IsMatch(value);

    internal static string ParseKwinLayout(string output)
    {
        var parts = output.Split('"');
        return parts.Length >= 3 && IsSafeLayoutId(parts[1]) ? parts[1] : string.Empty;
    }

    private Dictionary<string, string> ReadLayoutNames()
    {
        try
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var inLayouts = false;
            foreach (var line in File.ReadLines(_xkbRulesPath))
            {
                if (line.StartsWith("! layout", StringComparison.Ordinal))
                {
                    inLayouts = true;
                    continue;
                }

                if (inLayouts && line.StartsWith('!'))
                {
                    break;
                }

                if (!inLayouts || string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var fields = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 2 && IsSafeLayoutId(fields[0]))
                {
                    names.TryAdd(fields[0], fields[1]);
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    [GeneratedRegex("^[a-z0-9_+()-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex LayoutIdRegex();
}

public sealed record KeyboardLayoutOption(string Id, string DisplayName)
{
    public string Label => $"{DisplayName} ({Id})";
}

public sealed record KeyboardLayoutStatus(
    bool IsAvailable,
    string CurrentLayoutId,
    string SystemLayoutId,
    IReadOnlyList<KeyboardLayoutOption> AvailableLayouts,
    string Detail);

public sealed record KeyboardLayoutResult(bool Succeeded, string Message, bool AppliedToCurrentSession = false);
