using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Wayland.Server.Persistent;
using NWayland.Protocols.XdgActivationV1;

namespace Avalonia.Wayland;

/// <summary>Requests a single-use activation token from the real triggering input cookie.</summary>
public static class WaylandActivation
{
    public static Task<string> RequestAsync(PointerEventArgs input, string appId) =>
        input.PlatformInputEventCookie is WaylandInputEventCookie cookie ? RequestAsync(cookie, appId) : Task.FromResult("");

    public static Task<string> RequestAsync(KeyEventArgs input, string appId) =>
        input.PlatformInputEventCookie is WaylandInputEventCookie cookie ? RequestAsync(cookie, appId) : Task.FromResult("");

    private static async Task<string> RequestAsync(WaylandInputEventCookie cookie, string appId)
    {
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new TokenListener(result);
        cookie.RequestActivation(appId, listener);
        try { return await result.Task.WaitAsync(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false); }
        catch (TimeoutException) { cookie.PostOob(_ => listener.Cancel()); return ""; }
    }
    internal sealed class TokenListener(TaskCompletionSource<string> result) : XdgActivationTokenV1.Listener
    {
        private XdgActivationTokenV1? _proxy;
        internal void Attach(XdgActivationTokenV1 proxy) => _proxy = proxy;
        internal void Cancel() { _proxy?.Destroy(); _proxy = null; result.TrySetResult(""); }
        protected override void Done(XdgActivationTokenV1 sender, string token) { _proxy?.Destroy(); _proxy = null; result.TrySetResult(token); }
        internal void Unavailable() => result.TrySetResult("");
    }
}
