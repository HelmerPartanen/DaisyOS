namespace DaisyOS.Shell.Apps.Files.Models;

public sealed record FileRenameDelta(string OldPath, FileEntrySnapshot Snapshot);
