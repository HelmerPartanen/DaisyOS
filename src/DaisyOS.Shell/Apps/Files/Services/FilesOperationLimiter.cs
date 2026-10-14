namespace DaisyOS.Shell.Apps.Files.Services;

// Sequential work inside each operation, at most two write operations across all tabs.
internal static class FilesOperationLimiter
{
    private static readonly SemaphoreSlim Gate = new(2, 2);
    public static async Task<IDisposable> EnterAsync()
    {
        await Gate.WaitAsync();
        return new Release();
    }
    private sealed class Release : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) Gate.Release(); }
    }
}
