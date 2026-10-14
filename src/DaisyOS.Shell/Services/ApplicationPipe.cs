using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DaisyOS.Shell.Services.Serialization;

namespace DaisyOS.Shell.Services;

/// <summary>User-only request/reply transport for application activation and desktop preferences.</summary>
public static class ApplicationPipe
{
    // The pipe protocol uses one compact JSON value per line.
    private static readonly DaisyShellJsonContext WireJson = new(new JsonSerializerOptions { WriteIndented = false });
    public static string Name(string application) => "daisyos-" + application + "-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Environment.UserName + "|" + Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") + Diagnostics.ApplicationPerformanceProbe.InstanceSuffix)))[..16];

    public static async Task<string> RequestAsync(string name, string[] request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000, timeout.Token);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, WireJson.StringArray));
        return await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Application disconnected before replying.");
    }

    public static async Task ServeAsync(string name, Func<string[], Task<string>> handle, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(requestTimeout.Token);
                var request = line is null ? null : JsonSerializer.Deserialize(line, DaisyShellJsonContext.Default.StringArray);
                string response;
                try { response = request is null ? "Invalid request." : await handle(request); }
                catch (Exception ex) { response = ex.Message; }
                await writer.WriteLineAsync(response);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException)
            { Console.Error.WriteLine($"[{name}] Request failed: {ex.Message}"); }
        }
    }
}
