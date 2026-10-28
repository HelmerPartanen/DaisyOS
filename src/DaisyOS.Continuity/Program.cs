namespace DaisyOS.Continuity;
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsLinux()) return 1;
        if (args.Contains("--status"))
        {
            try
            {
                var status = await new DaisyOS.System.Continuity.ContinuityClient().ExecuteAsync(new("status"));
                Console.WriteLine(global::System.Text.Json.JsonSerializer.Serialize(status, DaisyOS.Core.Serialization.DaisyCoreJsonContext.Default.ContinuityStatus));
                return 0;
            }
            catch { Console.Error.WriteLine("Continuity service unavailable."); return 1; }
        }
        if (args.Contains("--clipboard-event")) return await ClipboardBridge.ForwardAsync();
        var root = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        root = string.IsNullOrEmpty(root) || !Path.IsPathFullyQualified(root) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state") : root;
        var statePath = Path.Combine(root, "daisyos", "continuity");
        var port = 47621;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--state-dir" && i + 1 < args.Length) statePath = args[++i];
            else if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
        }
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        using var sigterm = global::System.Runtime.InteropServices.PosixSignalRegistration.Create(global::System.Runtime.InteropServices.PosixSignal.SIGTERM,
            context => { context.Cancel = true; stop.Cancel(); });
        try
        {
            using var state = new SecureState(statePath);
            await using var daemon = new ContinuityDaemon(state, port);
            await daemon.RunAsync(stop.Token);
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception) { Console.Error.WriteLine("[continuity] startup_failed; inspect service configuration and private state permissions."); return 1; }
    }
}
