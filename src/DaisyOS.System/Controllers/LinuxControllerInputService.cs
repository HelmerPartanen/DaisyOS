using System.Text.Json;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Controllers;

/// <summary>
/// Reads the Linux joystick interface for the currently connected controller. It consumes only
/// the controller node; keyboard and mouse input remain entirely outside this service.
/// Keybindings are dynamically loaded from ~/.config/daisyos/controller_keybindings.json.
/// </summary>
public sealed class LinuxControllerInputService : IControllerInputService
{
    private const byte ButtonEvent = 0x01;
    private const byte AxisEvent = 0x02;
    private const byte InitialStateFlag = 0x80;
    private const ushort EvdevKeyEvent = 0x01;
    private const ushort EvdevAbsoluteAxisEvent = 0x03;
    private const short AxisThreshold = 16_000;
    private readonly object _sync = new();
    private readonly Dictionary<byte, ControllerNavigationAction> _activeAxes = [];
    private CancellationTokenSource? _readerCancellation;
    private Task? _readerTask;
    private string? _activeDevicePath;
    private bool _playStationLayout;
    private ControllerKeybindingsConfig _keybindingsConfig = new();
    private CancellationTokenSource? _openConsoleHoldCts;
    private bool _openConsoleHoldFired;

    public LinuxControllerInputService(bool loadUserConfig = true)
    {
        if (loadUserConfig)
        {
            LoadOrInitKeybindingsConfig();
        }
        else
        {
            _keybindingsConfig = new ControllerKeybindingsConfig();
        }
    }

    public event EventHandler<ControllerNavigationAction>? NavigationRequested;
    public event EventHandler<RawControllerInputEventArgs>? RawInputReceived;

    public bool IsRebinding { get; set; }

    public void ReloadKeybindings()
    {
        LoadOrInitKeybindingsConfig();
    }

    private void LoadOrInitKeybindingsConfig()
    {
        _keybindingsConfig = new ControllerKeybindingsConfig();
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configDir = Path.Combine(home, ".config", "daisyos");
            Directory.CreateDirectory(configDir);
            var configFile = Path.Combine(configDir, "controller_keybindings.json");

            if (File.Exists(configFile))
            {
                var json = File.ReadAllText(configFile);
                var loaded = JsonSerializer.Deserialize(json, DaisyOS.Core.Serialization.DaisyCoreJsonContext.Default.ControllerKeybindingsConfig);
                if (loaded != null)
                {
                    var defaults = new ControllerKeybindingsConfig();
                    foreach (var kvp in defaults.XboxButtons) loaded.XboxButtons.TryAdd(kvp.Key, kvp.Value);
                    foreach (var kvp in defaults.PlayStationButtons) loaded.PlayStationButtons.TryAdd(kvp.Key, kvp.Value);
                    foreach (var kvp in defaults.EvdevKeys) loaded.EvdevKeys.TryAdd(kvp.Key, kvp.Value);

                    // Ensure button 8 & 9 (Share/Options) never map to OpenConsole
                    if (loaded.PlayStationButtons.TryGetValue(8, out var p8) && p8 == "OpenConsole") loaded.PlayStationButtons.Remove(8);
                    if (loaded.PlayStationButtons.TryGetValue(9, out var p9) && p9 == "OpenConsole") loaded.PlayStationButtons.Remove(9);
                    if (loaded.XboxButtons.TryGetValue(8, out var x8) && x8 == "OpenConsole") loaded.XboxButtons.Remove(8);
                    loaded.PlayStationButtons[10] = "OpenConsole";
                    loaded.XboxButtons[10] = "OpenConsole";

                    _keybindingsConfig = loaded;
                    return;
                }
            }

            // Write default keybindings file if not exists or unreadable
            var defaultJson = JsonSerializer.Serialize(_keybindingsConfig, DaisyOS.Core.Serialization.DaisyCoreJsonContext.Default.ControllerKeybindingsConfig);
            File.WriteAllText(configFile, defaultJson);
        }
        catch
        {
            _keybindingsConfig = new ControllerKeybindingsConfig();
        }
    }

    public void Start(ControllerConnectionStatus controller)
    {
        var devicePath = controller.JoystickPath ?? controller.DevicePath;
        if (string.IsNullOrWhiteSpace(devicePath) || !File.Exists(devicePath))
        {
            Stop();
            return;
        }

        lock (_sync)
        {
            if (string.Equals(_activeDevicePath, devicePath, StringComparison.Ordinal) && _readerTask is { IsCompleted: false })
            {
                return;
            }

            StopReaderLocked();
            _activeDevicePath = devicePath;
            _playStationLayout = IsPlayStationController(controller.Name);
            _readerCancellation = new CancellationTokenSource();
            var usesJoystickProtocol = !string.IsNullOrWhiteSpace(controller.JoystickPath);
            _readerTask = Task.Run(() => usesJoystickProtocol
                ? ReadJoystickEventsAsync(devicePath, _readerCancellation.Token)
                : ReadEvdevEventsAsync(devicePath, _readerCancellation.Token));
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopReaderLocked();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? readerTask;
        lock (_sync)
        {
            readerTask = _readerTask;
            StopReaderLocked();
        }

        if (readerTask is not null)
        {
            try
            {
                await readerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shell shutdown.
            }
        }
    }

    private async Task ReadJoystickEventsAsync(string devicePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                devicePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 8,
                FileOptions.Asynchronous);
            var buffer = new byte[8];

            while (!cancellationToken.IsCancellationRequested)
            {
                var received = 0;
                while (received < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(received, buffer.Length - received), cancellationToken);
                    if (read == 0)
                    {
                        return;
                    }

                    received += read;
                }

                var state = DecodeNavigationWithState(buffer[6], BitConverter.ToInt16(buffer, 4), buffer[7], _playStationLayout);
                if (state is { } parsedState)
                {
                    ProcessAction(parsedState.Action, parsedState.IsPressed);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when a controller disconnects or the shell unloads.
        }
        catch (IOException)
        {
            // Controller removal closes the device stream on Linux.
        }
        catch (UnauthorizedAccessException)
        {
            // Detection remains useful even where session permissions do not grant input reads.
        }
    }

    private async Task ReadEvdevEventsAsync(string devicePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                devicePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 24,
                FileOptions.Asynchronous);
            var buffer = new byte[24];

            while (!cancellationToken.IsCancellationRequested)
            {
                var received = 0;
                while (received < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(received, buffer.Length - received), cancellationToken);
                    if (read == 0) return;
                    received += read;
                }

                var state = DecodeEvdevNavigationWithState(
                    BitConverter.ToUInt16(buffer, 16),
                    BitConverter.ToUInt16(buffer, 18),
                    BitConverter.ToInt32(buffer, 20));
                if (state is { } parsedState)
                {
                    ProcessAction(parsedState.Action, parsedState.IsPressed);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ProcessAction(ControllerNavigationAction action, bool isPressed)
    {
        if (IsRebinding) return;

        if (action == ControllerNavigationAction.OpenConsole)
        {
            if (isPressed)
            {
                _openConsoleHoldCts?.Cancel();
                _openConsoleHoldCts = new CancellationTokenSource();
                _openConsoleHoldFired = false;
                var token = _openConsoleHoldCts.Token;
                Task.Run(async () => 
                {
                    try
                    {
                        await Task.Delay(500, token);
                        _openConsoleHoldFired = true;
                        NavigationRequested?.Invoke(this, ControllerNavigationAction.OpenConsoleHold);
                    }
                    catch (TaskCanceledException)
                    {
                    }
                });
            }
            else
            {
                if (_openConsoleHoldCts != null)
                {
                    _openConsoleHoldCts.Cancel();
                    if (!_openConsoleHoldFired)
                    {
                        NavigationRequested?.Invoke(this, ControllerNavigationAction.OpenConsole);
                    }
                }
                _openConsoleHoldCts = null;
                _openConsoleHoldFired = false;
            }
        }
        else if (isPressed)
        {
            NavigationRequested?.Invoke(this, action);
        }
    }

    /// <summary>Maps a Linux js_event into a shell action. Kept public for deterministic tests.</summary>
    public (ControllerNavigationAction Action, bool IsPressed)? DecodeNavigationWithState(byte rawType, short value, byte number, bool playStationLayout = false)
    {
        var eventType = (byte)(rawType & ~InitialStateFlag);
        if (eventType == ButtonEvent)
        {
            RawInputReceived?.Invoke(this, new RawControllerInputEventArgs(eventType, number, value));
            var mapped = MapButton(number, playStationLayout);
            if (mapped.HasValue) return (mapped.Value, value == 1);
            return null;
        }

        if (eventType != AxisEvent)
        {
            return null;
        }

        var action = MapAxis(value, number);
        if (action is null)
        {
            _activeAxes.Remove(number);
            return null;
        }

        if (_activeAxes.TryGetValue(number, out var activeAction) && activeAction == action)
        {
            return null;
        }

        _activeAxes[number] = action.Value;
        return (action.Value, true);
    }

    public ControllerNavigationAction? DecodeNavigation(byte rawType, short value, byte number, bool playStationLayout = false)
    {
        var state = DecodeNavigationWithState(rawType, value, number, playStationLayout);
        return state is { IsPressed: true } ? state.Value.Action : null;
    }

    /// <summary>Maps the evdev fallback protocol used by event-only controllers.</summary>
    public (ControllerNavigationAction Action, bool IsPressed)? DecodeEvdevNavigationWithState(ushort type, ushort code, int value)
    {
        if (type == EvdevKeyEvent)
        {
            RawInputReceived?.Invoke(this, new RawControllerInputEventArgs(1, (byte)code, (short)value, code));

            ControllerNavigationAction? mappedAction = null;
            if (_keybindingsConfig.EvdevKeys.TryGetValue(code, out var actionName) &&
                Enum.TryParse<ControllerNavigationAction>(actionName, ignoreCase: true, out var parsedAction))
            {
                mappedAction = parsedAction;
            }
            else
            {
                mappedAction = code switch
                {
                    0x130 => ControllerNavigationAction.Confirm,         // BTN_SOUTH / A / Cross
                    0x131 => ControllerNavigationAction.Back,            // BTN_EAST / B / Circle
                    0x133 => ControllerNavigationAction.QuickSettings,   // BTN_WEST / X / Square
                    0x134 => ControllerNavigationAction.Details,         // BTN_NORTH / Y / Triangle
                    0x136 => ControllerNavigationAction.PreviousSection, // BTN_TL / LB
                    0x137 => ControllerNavigationAction.NextSection,     // BTN_TR / RB
                    0x13c or 0x13d => ControllerNavigationAction.OpenConsole, // BTN_MODE variants
                    _ => null
                };
            }
            
            if (mappedAction.HasValue) return (mappedAction.Value, value == 1);
            return null;
        }

        if (type != EvdevAbsoluteAxisEvent) return null;
        var action = code switch
        {
            0 or 6 or 16 when value < 0 => ControllerNavigationAction.Left,
            0 or 6 or 16 when value > 0 && (code == 16 || value > AxisThreshold) => ControllerNavigationAction.Right,
            1 or 7 or 17 when value < 0 => ControllerNavigationAction.Up,
            1 or 7 or 17 when value > 0 && (code == 17 || value > AxisThreshold) => ControllerNavigationAction.Down,
            _ => (ControllerNavigationAction?)null
        };

        if (action is null)
        {
            _activeAxes.Remove((byte)code);
            return null;
        }

        if (_activeAxes.TryGetValue((byte)code, out var activeAction) && activeAction == action)
        {
            return null;
        }

        _activeAxes[(byte)code] = action.Value;
        return (action.Value, true);
    }

    public ControllerNavigationAction? DecodeEvdevNavigation(ushort type, ushort code, int value)
    {
        var state = DecodeEvdevNavigationWithState(type, code, value);
        return state is { IsPressed: true } ? state.Value.Action : null;
    }

    private static ControllerNavigationAction? MapAxis(short value, byte axis) => axis switch
    {
        0 or 6 or 16 when value <= -AxisThreshold => ControllerNavigationAction.Left,
        0 or 6 or 16 when value >= AxisThreshold => ControllerNavigationAction.Right,
        1 or 7 or 17 when value <= -AxisThreshold => ControllerNavigationAction.Up,
        1 or 7 or 17 when value >= AxisThreshold => ControllerNavigationAction.Down,
        _ => null
    };

    private ControllerNavigationAction? MapButton(byte number, bool playStationLayout)
    {
        var buttonMap = playStationLayout ? _keybindingsConfig.PlayStationButtons : _keybindingsConfig.XboxButtons;
        if (buttonMap.TryGetValue(number, out var actionName) &&
            Enum.TryParse<ControllerNavigationAction>(actionName, ignoreCase: true, out var mappedAction))
        {
            return mappedAction;
        }

        return playStationLayout
            ? number switch
            {
                0 => ControllerNavigationAction.Confirm, // Cross (✕)
                1 => ControllerNavigationAction.Back, // Circle (○)
                3 => ControllerNavigationAction.QuickSettings, // Square (▫)
                2 => ControllerNavigationAction.Details, // Triangle (△)
                4 => ControllerNavigationAction.PreviousSection, // L1
                5 => ControllerNavigationAction.NextSection, // R1
                10 => ControllerNavigationAction.OpenConsole, // PS Logo Button ONLY
                _ => null
            }
            : number switch
            {
                0 => ControllerNavigationAction.Confirm, // A
                1 => ControllerNavigationAction.Back, // B
                2 => ControllerNavigationAction.QuickSettings, // X
                3 => ControllerNavigationAction.Details, // Y
                4 => ControllerNavigationAction.PreviousSection, // LB
                5 => ControllerNavigationAction.NextSection, // RB
                10 => ControllerNavigationAction.OpenConsole, // Xbox Guide Button ONLY
                _ => null
            };
    }

    private static bool IsPlayStationController(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Contains("sony", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dualshock", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dualsense", StringComparison.OrdinalIgnoreCase)
            || name.Contains("playstation", StringComparison.OrdinalIgnoreCase));

    private void StopReaderLocked()
    {
        _readerCancellation?.Cancel();
        _readerCancellation?.Dispose();
        _readerCancellation = null;
        _readerTask = null;
        _activeDevicePath = null;
        _playStationLayout = false;
        _activeAxes.Clear();
    }
}
