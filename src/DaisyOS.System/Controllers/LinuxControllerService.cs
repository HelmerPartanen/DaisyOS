using System.Text.RegularExpressions;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Controllers;

/// <summary>
/// Detects controllers recognized by the Linux input subsystem. This deliberately reads
/// /proc rather than Bluetooth pairing state: only a controller that created an input device
/// can switch the shell into console mode.
/// </summary>
public sealed class LinuxControllerService : IControllerService
{
    private static readonly Regex NameLine = new(@"^N:\s+Name=""(?<name>.*)""$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex HandlersLine = new(@"^H:\s+Handlers=(?<handlers>.*)$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly string[] ControllerNameHints =
    [
        "controller", "gamepad", "joystick", "dualsense", "dualshock", "xbox", "playstation",
        "nintendo", "joy-con", "joycon", "8bitdo", "stadia", "steam controller", "wireless gamepad"
    ];

    private readonly string _inputDevicesPath;

    public LinuxControllerService(string inputDevicesPath = "/proc/bus/input/devices")
    {
        _inputDevicesPath = inputDevicesPath;
    }

    public async Task<ControllerConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_inputDevicesPath))
            {
                return ControllerConnectionStatus.Disconnected;
            }

            var contents = await File.ReadAllTextAsync(_inputDevicesPath, cancellationToken);
            return ParseInputDevices(contents);
        }
        catch (IOException)
        {
            return ControllerConnectionStatus.Disconnected;
        }
        catch (UnauthorizedAccessException)
        {
            return ControllerConnectionStatus.Disconnected;
        }
    }

    /// <summary>Parses Linux's /proc/bus/input/devices format. Kept public for deterministic tests.</summary>
    public static ControllerConnectionStatus ParseInputDevices(string? contents)
    {
        if (string.IsNullOrWhiteSpace(contents))
        {
            return ControllerConnectionStatus.Disconnected;
        }

        foreach (var block in Regex.Split(contents.Trim(), @"(?:\r?\n){2,}"))
        {
            var nameMatch = NameLine.Match(block);
            var handlersMatch = HandlersLine.Match(block);
            if (!nameMatch.Success || !handlersMatch.Success)
            {
                continue;
            }

            var name = nameMatch.Groups["name"].Value.Trim();
            var handlers = handlersMatch.Groups["handlers"].Value;
            var tokens = handlers.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var joystickHandler = tokens.FirstOrDefault(token => token.StartsWith("js", StringComparison.Ordinal));
            var eventHandler = tokens.FirstOrDefault(token => token.StartsWith("event", StringComparison.Ordinal));
            var recognisableName = ControllerNameHints.Any(hint => name.Contains(hint, StringComparison.OrdinalIgnoreCase));

            // jsN is exposed for joystick-class input devices even if their vendor name is unknown.
            // A recognisable controller name covers modern devices that only expose eventN.
            if (recognisableName || joystickHandler is not null)
            {
                var handler = eventHandler ?? joystickHandler;
                return new ControllerConnectionStatus(
                    true,
                    string.IsNullOrWhiteSpace(name) ? "Game controller" : name,
                    handler is null ? null : $"/dev/input/{handler}",
                    joystickHandler is null ? null : $"/dev/input/{joystickHandler}");
            }
        }

        return ControllerConnectionStatus.Disconnected;
    }
}
