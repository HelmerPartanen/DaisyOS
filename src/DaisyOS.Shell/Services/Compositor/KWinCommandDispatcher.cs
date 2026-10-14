using System.Diagnostics;
using System.Text.Json;

namespace DaisyOS.Shell.Services.Compositor;

/// <summary>
/// Sends a single, validated window command to KWin through its scripting D-Bus
/// endpoint. Commands are short-lived scripts so the shell never assumes an
/// application process can be manipulated directly.
/// </summary>
internal sealed class KWinCommandDispatcher
{
    private const string KWinService = "org.kde.KWin";
    private const string ScriptingPath = "/Scripting";
    private const string ScriptingInterface = "org.kde.kwin.Scripting";

    public Task DispatchAsync(string windowId, KWinWindowCommand command, CancellationToken cancellationToken, int workspace = 0, bool wasActive = false)
    {
        if (string.IsNullOrWhiteSpace(windowId)
            || windowId.Length > 256
            || windowId.Any(char.IsControl))
        {
            return Task.CompletedTask;
        }

        Console.Error.WriteLine($"[daisy-window-bridge] Dispatching {command} for KWin window {windowId}.");
        return RunScriptAsync(CreateScript(windowId, command, workspace, wasActive), cancellationToken, "command");
    }

    /// <summary>
    /// Requests a complete KWin snapshot after the shell's dbus-monitor has
    /// subscribed. The packaged bridge normally publishes on lifecycle
    /// signals, but its startup snapshot can happen before a restarted shell
    /// is listening.
    /// </summary>
    public Task RequestSnapshotAsync(CancellationToken cancellationToken) =>
        RunScriptAsync(CreateSnapshotScript(), cancellationToken, "snapshot");

    /// <summary>
    /// Triggers the DaisyOS compositor-owned live task overview.
    /// </summary>
    public async Task ToggleOverviewEffectAsync(string effectName = "daisyos-task-overview-v15", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(effectName)) effectName = "daisyos-task-overview-v15";
        Console.Error.WriteLine($"[daisy-window-bridge] Toggling compositor effect {effectName}.");

        try
        {
            if (_dbusConnection is not null)
            {
                try
                {
                    // Ensure the effect is loaded in KWin
                    Tmds.DBus.Protocol.MessageBuffer loadMsg;
                    using (var loadWriter = _dbusConnection.GetMessageWriter())
                    {
                        loadWriter.WriteMethodCallHeader(
                            destination: KWinService,
                            path: "/Effects",
                            @interface: "org.kde.kwin.Effects",
                            member: "loadEffect",
                            signature: "s");
                        loadWriter.WriteString(effectName);
                        loadMsg = loadWriter.CreateMessage();
                    }
                    await _dbusConnection.CallMethodAsync(loadMsg);

                    // Invoke the shortcut registered by the loaded scene effect.
                    Tmds.DBus.Protocol.MessageBuffer invokeMsg;
                    using (var writer = _dbusConnection.GetMessageWriter())
                    {
                        writer.WriteMethodCallHeader(
                            destination: "org.kde.kglobalaccel",
                            path: "/component/kwin",
                            @interface: "org.kde.kglobalaccel.Component",
                            member: "invokeShortcut",
                            signature: "s");
                        writer.WriteString("DaisyOS Task Overview");
                        invokeMsg = writer.CreateMessage();
                    }
                    await _dbusConnection.CallMethodAsync(invokeMsg);
                    return;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[daisy-window-bridge] Direct D-Bus invokeShortcut failed, falling back to qdbus: {ex.Message}");
                }
            }

            await RunQDbusAsync(
                [KWinService, "/Effects", "org.kde.kwin.Effects.loadEffect", effectName],
                cancellationToken,
                "loadEffect");
            await RunQDbusAsync(
                ["org.kde.kglobalaccel", "/component/kwin", "org.kde.kglobalaccel.Component.invokeShortcut", "DaisyOS Task Overview"],
                cancellationToken,
                "invokeShortcut");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[daisy-window-bridge] Could not toggle KWin effect '{effectName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Last-resort termination for a client KWin has already classified as
    /// unresponsive. The caller owns all policy checks; this method rejects
    /// unsafe process identities before issuing a process kill.
    /// </summary>
    public async Task<bool> ForceQuitAsync(int processId, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()
            || processId <= 1
            || processId == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return true;
            }

            process.Kill(entireProcessTree: true);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token);
            Console.Error.WriteLine($"[daisy-window-bridge] Force-quit unresponsive process {processId}.");
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine($"[daisy-window-bridge] Timed out while force-quitting process {processId}.");
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or global::System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"[daisy-window-bridge] Could not force-quit process {processId}: {exception.Message}");
            return false;
        }
    }

    private static Tmds.DBus.Protocol.DBusConnection? _dbusConnection;

    public static void SetConnection(Tmds.DBus.Protocol.DBusConnection? connection) =>
        _dbusConnection = connection;

    private static async Task RunScriptAsync(string script, CancellationToken cancellationToken, string purpose)
    {
        var runtimeDirectory = Path.Combine(
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath(),
            "daisyos", "kwin-commands");
        Directory.CreateDirectory(runtimeDirectory);

        var pluginName = $"daisyos-{purpose}-{Guid.NewGuid():N}";
        var scriptPath = Path.Combine(runtimeDirectory, $"{pluginName}.js");
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        try
        {
            if (_dbusConnection is not null)
            {
                try
                {
                    Tmds.DBus.Protocol.MessageBuffer loadMsg;
                    using (var loadWriter = _dbusConnection.GetMessageWriter())
                    {
                        loadWriter.WriteMethodCallHeader(
                            destination: KWinService,
                            path: ScriptingPath,
                            @interface: ScriptingInterface,
                            member: "loadScript",
                            signature: "ss");
                        loadWriter.WriteString(scriptPath);
                        loadWriter.WriteString(pluginName);
                        loadMsg = loadWriter.CreateMessage();
                    }
                    await _dbusConnection.CallMethodAsync(loadMsg);

                    Tmds.DBus.Protocol.MessageBuffer startMsg;
                    using (var startWriter = _dbusConnection.GetMessageWriter())
                    {
                        startWriter.WriteMethodCallHeader(
                            destination: KWinService,
                            path: ScriptingPath,
                            @interface: ScriptingInterface,
                            member: "start");
                        startMsg = startWriter.CreateMessage();
                    }
                    await _dbusConnection.CallMethodAsync(startMsg);
                    return;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[daisy-window-bridge] Direct D-Bus call failed, falling back to qdbus: {ex.Message}");
                }
            }

            // Fallback to qdbus6 if D-Bus connection is not available
            await RunQDbusAsync(
                [KWinService, ScriptingPath, $"{ScriptingInterface}.loadScript", scriptPath, pluginName],
                cancellationToken,
                purpose);
            await RunQDbusAsync(
                [KWinService, ScriptingPath, $"{ScriptingInterface}.start"],
                cancellationToken,
                purpose);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch (IOException) { }
        }
    }

    private static string CreateScript(string windowId, KWinWindowCommand command, int workspace, bool wasActive = false)
    {
        var literalId = JsonSerializer.Serialize(windowId, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.String);
        var body = command switch
        {
            KWinWindowCommand.ActivateOrMinimize =>
                // Use the shell's pre-click snapshot state instead of querying
                // workspace.activeWindow at script-execution time. By the time
                // loadScript+start has run through two qdbus round-trips, the
                // layer-shell taskbar button may have already stolen focus, so
                // workspace.activeWindow no longer points at the target window
                // and the minimize condition fails. The shell already knows
                // IsActive from its last compositor snapshot — pass it in.
                wasActive
                    ? "target.minimized = true;"
                    : "target.minimized = false; workspace.activeWindow = target;",
            KWinWindowCommand.Close => "target.closeWindow();",
            KWinWindowCommand.Maximize => "target.setMaximize(true, true);",
            KWinWindowCommand.Restore => "target.setMaximize(false, false); target.minimized = false; workspace.activeWindow = target;",
            KWinWindowCommand.MoveToWorkspace => $@"
var targetWorkspace = Number({workspace});
if (targetWorkspace > 0 && workspace.desktops.length >= targetWorkspace) {{
    target.desktops = [workspace.desktops[targetWorkspace - 1]];
}}
",
            _ => string.Empty
        };

        return $@"
var targetId = {literalId};
var windows = typeof workspace.windowList === 'function'
    ? workspace.windowList()
    : (typeof workspace.clientList === 'function' ? workspace.clientList() : []);
for (var index = 0; index < windows.length; ++index) {{
    var candidate = windows[index];
    if (String(candidate.internalId) !== targetId) continue;
    var target = candidate;
    {body}
    break;
}}
";
    }

    private static string CreateSnapshotScript() => $@"
var bridgeService = {JsonSerializer.Serialize(KWinWindowBridge.BusName, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.String)};
var bridgePath = {JsonSerializer.Serialize(KWinWindowBridge.ObjectPath, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.String)};
var bridgeInterface = {JsonSerializer.Serialize(KWinWindowBridge.InterfaceName, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.String)};
var daisyTitles = {{ settings: 'settings', notes: 'notes', calendar: 'calendar', calculator: 'calculator', files: 'files', 'disk manager': 'diskmanager', 'app store': 'appstore', sovelluskauppa: 'appstore' }};
var daisyAppIds = {{ 'org.daisyos.settings': 'settings', 'org.daisyos.notes': 'notes', 'org.daisyos.calendar': 'calendar', 'org.daisyos.calculator': 'calculator', 'org.daisyos.files': 'files', 'org.daisyos.diskmanager': 'diskmanager', 'org.daisyos.appstore': 'appstore' }};
function value(v) {{ return v === undefined || v === null ? '' : String(v); }}
function daisyApp(w) {{
    var resource = value(w.resourceClass).toLowerCase();
    var desktop = value(w.desktopFileName).toLowerCase();
    var resourceName = value(w.resourceName).toLowerCase();
    var identities = [desktop, resource, resourceName];
    for (var index = 0; index < identities.length; ++index) if (daisyAppIds[identities[index]]) return daisyAppIds[identities[index]];
    var isDaisy = resource.indexOf('daisyos-shell') >= 0 || resource.indexOf('daisyos.shell') >= 0 || resourceName.indexOf('daisyos-shell') >= 0 || resourceName.indexOf('daisyos.shell') >= 0 || desktop.indexOf('daisyos-shell') >= 0;
    if (!isDaisy) return '';
    var title = value(w.caption).toLowerCase();
    for (var key in daisyTitles) if (title === daisyTitles[key]) return key;
    return '';
}}
var source = typeof workspace.windowList === 'function'
    ? workspace.windowList()
    : (typeof workspace.clientList === 'function' ? workspace.clientList() : []);
var activeWindow = workspace.activeWindow;
var activeId = activeWindow ? value(activeWindow.internalId) : '';
var windows = [];
for (var index = 0; index < source.length; ++index) {{
    var w = source[index];
    if (!w || w.internal || w.skipTaskbar || w.managed === false) continue;
    // KWin can classify an ordinary native xdg_toplevel outside its
    // normalWindow/dialog convenience flags.  Exclude only genuine special
    // or popup surfaces, matching the persistent KWin bridge filter.
    if (w.specialWindow === true || w.popupWindow === true) continue;
    var owned = daisyApp(w);
    if (value(w.caption).toLowerCase() === 'daisyos shell' || (!owned && (value(w.resourceClass).toLowerCase().indexOf('daisyos.shell') >= 0 || value(w.resourceClass).toLowerCase().indexOf('daisyos-shell') >= 0))) continue;
    var desktopFileId = value(w.desktopFileName);
    windows.push({{
        id: value(w.internalId),
        appId: desktopFileId || value(w.resourceClass) || value(w.resourceName) || 'application',
        title: value(w.caption),
        desktopFileId: desktopFileId || null,
        iconName: value(w.icon) || null,
        outputName: w.output ? value(w.output.name) : null,
        workspace: w.desktops && w.desktops.length > 0 ? Number(w.desktops[0].x11DesktopNumber || 0) : 0,
        isActive: activeId === value(w.internalId),
        isMinimized: !!w.minimized,
        isMaximized: Number(w.maximizeMode) === 3,
        isFullScreen: !!w.fullScreen,
        isOnCurrentWorkspace: w.onAllDesktops || !w.desktops || w.desktops.length === 0 || w.desktops.some(function (desktop) {{ return desktop.id === workspace.currentDesktop.id; }}),
        canClose: !!w.closeable,
        daisyAppId: owned || null,
        processId: Number(w.pid || 0),
        isUnresponsive: !!w.unresponsive
    }});
}}
var covering = source.filter(function (w) {{
    return w && !w.internal && w.managed !== false && w.specialWindow !== true && w.popupWindow !== true
        && !w.minimized && (w.onAllDesktops || !w.desktops || w.desktops.length === 0 || w.desktops.some(function (d) {{ return d.id === workspace.currentDesktop.id; }}));
}});
callDBus(bridgeService, bridgePath, bridgeInterface, 'PublishSnapshot', JSON.stringify({{ windows: windows,
    desktopOccluded: covering.some(function (w) {{ return w.fullScreen || Number(w.maximizeMode) === 3; }}),
    fullscreenActive: covering.some(function (w) {{ return !!w.fullScreen; }}) }}), function () {{}});
";

    private static async Task<string> RunQDbusAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken, string purpose)
    {
        if (!OperatingSystem.IsLinux()) return string.Empty;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "qdbus6",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode == 0)
            {
                return (await output).Trim();
            }

            Console.Error.WriteLine($"[daisy-window-bridge] KWin {purpose} command failed ({process.ExitCode}): {(await error).Trim()}");
            return string.Empty;
        }
        catch (Exception exception) when (OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine($"[daisy-window-bridge] Could not send KWin {purpose} command: {exception.Message}");
            return string.Empty;
        }
    }
}

internal enum KWinWindowCommand
{
    ActivateOrMinimize,
    Close,
    Maximize,
    Restore,
    MoveToWorkspace
}
