namespace DaisyOS.Shell.Apps.Files.Services;

// Bounds survive session disposal: an old native decode can finish, while
// canceled requests waiting for a slot never start filesystem work.
internal static class FilesIoScheduler
{
    public static int VisualWorkerLimit { get; } = int.TryParse(Environment.GetEnvironmentVariable("DAISYOS_FILES_VISUAL_WORKERS"), out var workers) ? Math.Clamp(workers, 1, 4) : 2;
    private static readonly SemaphoreSlim VisualGate = new(VisualWorkerLimit, VisualWorkerLimit);
    private static readonly SemaphoreSlim MetadataGate = new(2, 2);

    public static async Task<T> ReadMetadataAsync<T>(Func<T> read, CancellationToken token)
    {
        await MetadataGate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(read, token).ConfigureAwait(false); }
        finally { MetadataGate.Release(); }
    }
    public static async Task<T> LoadVisualAsync<T>(Func<Task<T>> load, CancellationToken token)
    {
        await VisualGate.WaitAsync(token).ConfigureAwait(false);
        try { token.ThrowIfCancellationRequested(); return await load().ConfigureAwait(false); }
        finally { VisualGate.Release(); }
    }
}
