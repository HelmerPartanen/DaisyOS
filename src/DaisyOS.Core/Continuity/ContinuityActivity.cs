namespace DaisyOS.Core.Continuity;

/// <summary>A portable content snapshot, not process state or a remote command.</summary>
public sealed record ContinuityActivity(string AppId, string Kind, int SchemaVersion, string Title, string Text)
{
    public void Validate()
    {
        if (AppId != "org.daisyos.notes" || Kind != "note.text" || SchemaVersion != 1 ||
            Title is null || Text is null || Title.Length > 256 || Text.Length > 64 * 1024 || Title.Any(char.IsControl) || Text.Contains('\0'))
            throw new InvalidDataException("Unsupported or oversized activity.");
    }
}
