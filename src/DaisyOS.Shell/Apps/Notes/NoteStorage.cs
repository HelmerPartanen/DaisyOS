using System;
using System.IO;
using System.Text;
using DaisyOS.Shell.Apps.Notes.Documents;

namespace DaisyOS.Shell.Apps.Notes;

internal static class NoteStorage
{
    public static string Save(string directory, NoteItem note)
    {
        var isNew = string.IsNullOrEmpty(note.FilePath);
        var title = string.IsNullOrWhiteSpace(note.Title) ? Loc.Get("Untitled") : note.Title;
        foreach (var character in Path.GetInvalidFileNameChars()) title = title.Replace(character, '_');

        if (title.Length > 80) title = title[..80].TrimEnd();
        if (title.Length > 0 && char.IsHighSurrogate(title[^1])) title = title[..^1];
        var ext = note.IsRichNote ? FormatDetector.RichNoteExtension : note.PreferredExtension;
        var path = isNew
            ? Path.Combine(directory, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{title}_{Guid.NewGuid():N}{ext}")
            : note.FilePath!;

        if (note.IsRichNote && note.Package != null)
        {
            NotePackageFormat.Write(path, note.Package.Document, note.Package.Assets);
            return path;
        }

        // Text-based files
        var target = File.Exists(path)
            ? new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path : path;
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".daisy-note-{Guid.NewGuid():N}.tmp");
        try
        {
            if (!isNew && File.Exists(target))
            {
                using var writable = File.Open(target, FileMode.Open, FileAccess.Write, FileShare.Read);
            }

            var bytes = note.FileContent != null
                ? TextFileCodec.Encode(note.FileContent, note.Content)
                : new UTF8Encoding(false).GetBytes(note.Content);

            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporary, File.Exists(target)
                        ? File.GetUnixFileMode(target) : UnixFileMode.UserRead | UnixFileMode.UserWrite);

                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: !isNew);
            return path;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
