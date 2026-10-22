using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DaisyOS.Core.Models;

namespace DaisyOS.System.Bluetooth;

/// <summary>Versioned, per-user IPC provided by the pinned LibrePods companion build.</summary>
public sealed class LibrePodsService
{
    private readonly string? _socketPath;
    public LibrePodsService(string? socketPath = null)
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        _socketPath = socketPath ?? (string.IsNullOrEmpty(runtime) ? null : Path.Combine(runtime, "daisyos-librepods.sock"));
    }

    public async Task<AirPodsStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync("status", cancellationToken);
        if (response is null) return AirPodsStatus.Unavailable;
        try
        {
            using var json = JsonDocument.Parse(response);
            var root = json.RootElement;
            if (root.GetProperty("version").GetInt32() != 1) return AirPodsStatus.Unavailable;
            var mode = root.GetProperty("mode").GetInt32();
            var modes = root.GetProperty("modes").EnumerateArray().Select(x => x.GetInt32())
                .Where(x => x is >= 0 and <= 3).Select(x => (AirPodsNoiseMode)x).ToArray();
            return new(true, root.GetProperty("connected").GetBoolean(), root.GetProperty("address").GetString() ?? "",
                root.GetProperty("name").GetString() ?? "AirPods", mode is >= 0 and <= 3 ? (AirPodsNoiseMode)mode : null,
                modes, root.GetProperty("battery").GetString() ?? "",
                root.TryGetProperty("nearbyAddress", out var nearby) ? nearby.GetString() ?? "" : "");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return AirPodsStatus.Unavailable;
        }
    }

    public async Task<bool> SetNoiseModeAsync(string address, AirPodsNoiseMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode) || !global::System.Text.RegularExpressions.Regex.IsMatch(address, @"^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$")) return false;
        var response = await RequestAsync($"noise:{address}:{(int)mode}", cancellationToken);
        if (response != "ok") return false;
        // A socket write is not confirmation: wait for the AirPods' own mode report.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await Task.Delay(250, cancellationToken);
            var status = await GetStatusAsync(cancellationToken);
            if (!status.IsConnected || !string.Equals(status.Address, address, StringComparison.OrdinalIgnoreCase)) return false;
            if (status.NoiseMode == mode) return true;
        }
        return false;
    }

    private async Task<string?> RequestAsync(string command, CancellationToken cancellationToken)
    {
        if (_socketPath is null) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), timeout.Token);
            using var stream = new NetworkStream(socket);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\n"), timeout.Token);
            var buffer = new byte[8192];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token);
                if (read == 0) break;
                length += read;
                var newline = Array.IndexOf(buffer, (byte)'\n', 0, length);
                if (newline >= 0) return Encoding.UTF8.GetString(buffer, 0, newline);
            }
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is SocketException or IOException) { return null; }
    }
}
