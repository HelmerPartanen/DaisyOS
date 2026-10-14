using System.IO;
using Avalonia.Threading;
using DaisyOS.Shell.Apps.Files.Models;

namespace DaisyOS.Shell.Apps.Files.Services;

public sealed class DirectoryMutationCoordinator(
    DirectorySnapshotService snapshots,
    Func<DirectorySession?> current,
    Action<DirectorySession, DirectoryDelta> apply,
    IFileUiDispatcher dispatcher)
{
    public async Task PublishPathsAsync(DirectorySession? session, IEnumerable<string> paths,
        IEnumerable<string>? removed = null, IEnumerable<(string OldPath, string NewPath)>? renamed = null)
    {
        if (session == null || session.IsDisposed) return;
        var changes = paths.Where(p => IsChild(session.Path, p)).Distinct(StringComparer.Ordinal).ToArray();
        var deletions = (removed ?? []).Where(p => IsChild(session.Path, p)).Distinct(StringComparer.Ordinal).ToList();
        var moves = (renamed ?? []).Where(p => IsChild(session.Path, p.OldPath) && IsChild(session.Path, p.NewPath)).ToArray();
        try
        {
            var token = session.Token;
            var delta = await FilesIoScheduler.ReadMetadataAsync(() =>
            {
                List<FileEntrySnapshot> added = [];
                List<FileRenameDelta> renames = [];
                foreach (var path in changes)
                {
                    token.ThrowIfCancellationRequested();
                    if (snapshots.Read(path) is { } snapshot) added.Add(snapshot);
                    else deletions.Add(path);
                }
                foreach (var move in moves)
                {
                    token.ThrowIfCancellationRequested();
                    if (snapshots.Read(move.NewPath) is { } snapshot) renames.Add(new(move.OldPath, snapshot));
                    else deletions.Add(move.OldPath);
                }
                return new DirectoryDelta(added, deletions, renames, []);
            }, token);
            await dispatcher.InvokeAsync(() =>
            {
                if (!session.IsDisposed && ReferenceEquals(current(), session)) apply(session, delta);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable metadata is not evidence that an existing file was deleted.
            session.Watcher.Enqueue(new(DirectoryEventKind.Failure, session.Path));
        }
    }
    private static bool IsChild(string directory, string path) =>
        string.Equals(DirectoryIndex.Normalize(directory), Path.GetDirectoryName(DirectoryIndex.Normalize(path)), StringComparison.Ordinal);
}
