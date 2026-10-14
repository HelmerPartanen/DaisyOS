using DaisyOS.Core;
using Xunit;

namespace DaisyOS.Tests;

public sealed class AtomicTextFileTests
{
    [Fact]
    public async Task ConcurrentWritersAndReadersOnlySeeCompleteDocuments()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-atomic-" + Guid.NewGuid());
        var path = Path.Combine(directory, "settings.json");
        var values = Enumerable.Range(0, 32).Select(i => i + new string('x', 65536)).ToArray();
        try
        {
            AtomicTextFile.Write(path, values[0]);
            var writes = Task.WhenAll(values.Select(value => AtomicTextFile.WriteAsync(path, value)));
            while (!writes.IsCompleted) Assert.Contains(await File.ReadAllTextAsync(path), values);
            await writes;
            Assert.Contains(await File.ReadAllTextAsync(path), values);
            Assert.Single(Directory.EnumerateFiles(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CancelledSavePreservesPreviousFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-atomic-cancel-" + Guid.NewGuid());
        var path = Path.Combine(directory, "settings.json");
        try
        {
            AtomicTextFile.Write(path, "previous");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicTextFile.WriteAsync(path, "replacement", new CancellationToken(true)));
            Assert.Equal("previous", File.ReadAllText(path));
            Assert.Single(Directory.EnumerateFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FailedCommitCleansItsTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-atomic-failure-" + Guid.NewGuid());
        var path = Path.Combine(directory, "destination");
        Directory.CreateDirectory(path);
        try
        {
            await Assert.ThrowsAnyAsync<IOException>(() => AtomicTextFile.WriteAsync(path, "owned"));
            Assert.Empty(Directory.EnumerateFiles(directory));
            Assert.True(Directory.Exists(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
