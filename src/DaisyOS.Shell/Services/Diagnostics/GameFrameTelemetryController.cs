using System.Net.Sockets;
using System.Text;

namespace DaisyOS.Shell.Services.Diagnostics;

/// <summary>Controls hidden MangoHud capture only while the monitor is visible.</summary>
internal sealed class GameFrameTelemetryController(string socketTable = "/proc/net/unix", string? userName = null)
{
    private readonly string _prefix = "daisyos-fps-" + (userName ?? Environment.UserName) + "-";
    private int _capturedPid;

    internal IEnumerable<int> AvailableProcesses()
    {
        try
        {
            return File.ReadLines(socketTable).Select(line => line[(line.LastIndexOf(' ') + 1)..])
                .Where(path => path.StartsWith("@" + _prefix, StringComparison.Ordinal))
                .Select(path => int.TryParse(path.AsSpan(_prefix.Length + 1), out var pid) ? pid : 0)
                .Where(pid => pid > 0).Distinct().ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    public async Task UpdateAsync(bool visible, int preferredPid, CancellationToken cancellationToken)
    {
        var available = visible ? AvailableProcesses().ToHashSet() : [];
        var desired = available.Contains(preferredPid) ? preferredPid :
            available.Contains(_capturedPid) ? _capturedPid : available.OrderDescending().FirstOrDefault();
        if (desired == _capturedPid) return;
        if (_capturedPid > 0) await RequestAsync(_capturedPid, false, cancellationToken).ConfigureAwait(false);
        _capturedPid = 0;
        if (desired > 0 && await RequestAsync(desired, true, cancellationToken).ConfigureAwait(false)) _capturedPid = desired;
    }

    private async Task<bool> RequestAsync(int pid, bool enabled, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(500));
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint("\0" + _prefix + pid), deadline.Token).ConfigureAwait(false);
            var greeting = new byte[4096];
            if (await socket.ReceiveAsync(greeting, SocketFlags.None, deadline.Token).ConfigureAwait(false) == 0) return false;
            var request = Encoding.ASCII.GetBytes(enabled ? ":logging=1;" : ":logging=0;");
            var sent = 0;
            while (sent < request.Length)
            {
                var count = await socket.SendAsync(request.AsMemory(sent), SocketFlags.None, deadline.Token).ConfigureAwait(false);
                if (count == 0) return false;
                sent += count;
            }
            // Give the render thread time to consume the request before disconnecting.
            await Task.Delay(50, deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException) { return false; }
    }
}
