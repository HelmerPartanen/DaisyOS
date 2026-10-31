using System.Net.NetworkInformation;
using Tmds.DBus.Protocol;

namespace DaisyOS.Continuity;

public sealed partial class ContinuityDaemon
{
    private DBusConnection? _loginBus;
    private IDisposable? _sessionWatch;
    private IDisposable? _sleepWatch;
    private string _sessionId = "";
    private bool _sessionGranted;
    private bool _preparingForSleep;
    private long _sessionRevision;
    private async Task StartSessionGuardAsync()
    {
        _loginBus = new DBusConnection(DBusAddress.System ?? "unix:path=/run/dbus/system_bus_socket");
        await _loginBus.ConnectAsync();
        _ = FailClosedOnDisconnectAsync(_loginBus);
        _sessionWatch = await _loginBus.AddMatchAsync<int>(new MatchRule { Type = MessageType.Signal, Sender = "org.freedesktop.login1",
            Interface = "org.freedesktop.DBus.Properties", Member = "PropertiesChanged" }, (message, _) => 0,
            notification => { if (notification.HasValue) Track(RevalidateSessionAsync); }, emitOnCapturedContext: false);
        _sleepWatch = await _loginBus.AddMatchAsync<bool>(new MatchRule { Type = MessageType.Signal, Sender = "org.freedesktop.login1",
            Interface = "org.freedesktop.login1.Manager", Member = "PrepareForSleep" }, (message, _) => message.GetBodyReader().ReadBool(),
            notification =>
            {
                if (!notification.HasValue) return;
                PrepareForSleep(notification.Value);
            }, emitOnCapturedContext: false);
        NetworkChange.NetworkAddressChanged += NetworkChanged;
    }
    internal void PrepareForSleep(bool sleeping)
    {
        lock (_gate)
        {
            _preparingForSleep = sleeping;
            _sessionRevision++;
            if (sleeping) { _sessionReady = false; StopSharing(); }
        }
        if (!sleeping) Track(RevalidateSessionAsync);
    }
    private void NetworkChanged(object? sender, EventArgs e) => Track(RefreshVisibilityAsync);
    private async Task RevalidateSessionAsync()
    {
        string id; long revision;
        lock (_gate) { if (_preparingForSleep || !_sessionGranted || _shellOwner is null) return; id = _sessionId; revision = ++_sessionRevision; }
        var valid = await _sessionUnlocked(id);
        lock (_gate)
        {
            if (_preparingForSleep || !_sessionGranted || _shellOwner is null || id != _sessionId || revision != _sessionRevision) return;
            var wasReady = Ready;
            _sessionReady = valid;
            if (!valid) StopSharing();
            else { UpdateClipboardState(); _ = RefreshVisibilityAsync(); if (!wasReady) Track(ReconcilePeersAsync); }
        }
    }
    private async Task FailClosedOnDisconnectAsync(DBusConnection bus)
    {
        await bus.DisconnectedAsync();
        lock (_gate)
        {
            if (_disposing) return;
            _sessionRevision++; _sessionGranted = false; _sessionReady = false; StopSharing(); _stop.Cancel();
        }
    }
}
