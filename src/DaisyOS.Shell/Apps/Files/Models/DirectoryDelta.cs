namespace DaisyOS.Shell.Apps.Files.Models;

public sealed record DirectoryDelta(
    IReadOnlyList<FileEntrySnapshot> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<FileRenameDelta> Renamed,
    IReadOnlyList<FileEntrySnapshot> Changed)
{
    public static readonly DirectoryDelta Empty = new([], [], [], []);
    public int Count => Added.Count + Removed.Count + Renamed.Count + Changed.Count;
}
