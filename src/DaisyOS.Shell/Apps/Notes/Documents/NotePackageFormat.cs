using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaisyOS.Shell.Apps.Notes.Documents;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(NoteDocument))]
internal sealed partial class NotesJsonContext : JsonSerializerContext
{
}

public sealed class NotePackageException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A loaded .dnote: the document plus the image bytes it references.</summary>
public sealed class NotePackage
{
    public NoteDocument Document { get; set; } = NoteDocument.CreateEmpty();
    public Dictionary<string, byte[]> Assets { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Reads and writes the .dnote format: a zip archive containing
/// <c>mimetype</c>, <c>content.json</c>, <c>preview.txt</c> (plain-text
/// mirror used for fast search and by other apps) and <c>assets/*</c>.
/// </summary>
public static class NotePackageFormat
{
    public const string MimeType = "application/x-daisyos-note";
    private const string MimeTypeEntry = "mimetype";
    private const string ContentEntry = "content.json";
    private const string PreviewEntry = "preview.txt";
    private const string AssetsPrefix = "assets/";
    private const long MaxContentBytes = 64L * 1024 * 1024;
    private const long MaxAssetBytes = 256L * 1024 * 1024;

    public static NotePackage Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static NotePackage Read(Stream stream)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var contentEntry = archive.GetEntry(ContentEntry)
                ?? throw new NotePackageException("This note is damaged: its content is missing.");
            if (contentEntry.Length > MaxContentBytes)
                throw new NotePackageException("This note is too large to open.");

            NoteDocument document;
            using (var content = contentEntry.Open())
            {
                document = JsonSerializer.Deserialize(content, NotesJsonContext.Default.NoteDocument)
                    ?? throw new NotePackageException("This note is damaged: its content is empty.");
            }
            if (document.SchemaVersion > NoteDocument.CurrentSchemaVersion)
                throw new NotePackageException("This note was created by a newer version of DaisyOS Notes.");
            Sanitize(document);

            var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(AssetsPrefix, StringComparison.Ordinal)) continue;
                var name = entry.FullName[AssetsPrefix.Length..];
                if (!IsSafeAssetName(name) || entry.Length > MaxAssetBytes) continue;
                using var assetStream = entry.Open();
                using var buffer = new MemoryStream((int)entry.Length);
                assetStream.CopyTo(buffer);
                assets[name] = buffer.ToArray();
            }
            return new NotePackage { Document = document, Assets = assets };
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            throw new NotePackageException("This note is damaged and can't be opened.", ex);
        }
    }

    /// <summary>Reads only the plain-text mirror; used to search without parsing documents.</summary>
    public static string ReadPreview(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.GetEntry(PreviewEntry);
            if (entry is null) return string.Empty;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Writes atomically: the package is fully written to a temporary file in
    /// the same directory, flushed, then moved over the destination.
    /// Unreferenced assets are dropped so deleted images do not linger.
    /// </summary>
    public static void Write(string path, NoteDocument document, IReadOnlyDictionary<string, byte[]> assets)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var target = File.Exists(path)
            ? new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path : path;
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".daisy-note-{Guid.NewGuid():N}.tmp");
        try
        {
            if (File.Exists(target))
            {
                // Respect read-only notes: rename permission alone must not replace them.
                using var writable = File.Open(target, FileMode.Open, FileAccess.Write, FileShare.Read);
            }
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporary, File.Exists(target)
                        ? File.GetUnixFileMode(target) : UnixFileMode.UserRead | UnixFileMode.UserWrite);
                WriteTo(stream, document, assets);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void WriteTo(Stream stream, NoteDocument document, IReadOnlyDictionary<string, byte[]> assets)
    {
        foreach (var block in document.Blocks) block.Normalize();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        // Uncompressed first entry, as in ODF/EPUB, so tools can sniff the type.
        WriteText(archive, MimeTypeEntry, MimeType, CompressionLevel.NoCompression);

        var contentEntry = archive.CreateEntry(ContentEntry, CompressionLevel.Optimal);
        using (var content = contentEntry.Open())
            JsonSerializer.Serialize(content, document, NotesJsonContext.Default.NoteDocument);

        WriteText(archive, PreviewEntry, document.ToPlainText(), CompressionLevel.Optimal);

        foreach (var name in document.ReferencedAssets())
        {
            if (!IsSafeAssetName(name) || !assets.TryGetValue(name, out var bytes)) continue;
            // Images are already compressed; recompressing only costs time.
            var entry = archive.CreateEntry(AssetsPrefix + name, CompressionLevel.NoCompression);
            using var output = entry.Open();
            output.Write(bytes);
        }
    }

    /// <summary>Creates a collision-free asset name that keeps the original extension.</summary>
    public static string CreateAssetName(string sourceFileName)
    {
        var extension = Path.GetExtension(sourceFileName).ToLowerInvariant();
        if (extension.Length is 0 or > 6 || extension.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.'))
            extension = ".png";
        return $"{Guid.NewGuid():N}{extension}";
    }

    internal static bool IsSafeAssetName(string name) =>
        name.Length is > 0 and <= 128
        && name.IndexOfAny(['/', '\\', ':', '\0']) < 0
        && name != "." && name != "..";

    private static void WriteText(ZipArchive archive, string name, string text, CompressionLevel level)
    {
        var entry = archive.CreateEntry(name, level);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    /// <summary>Clamps values from untrusted files to ranges the editor can render.</summary>
    private static void Sanitize(NoteDocument document)
    {
        document.Blocks ??= [];
        document.Defaults ??= new NoteDefaults();
        document.Defaults.FontSize = Math.Clamp(document.Defaults.FontSize, 6, 200);
        document.Defaults.FontWeight = Math.Clamp(document.Defaults.FontWeight, 1, 1000);
        document.Defaults.LineHeight = Math.Clamp(document.Defaults.LineHeight, 0.8, 4);
        foreach (var block in document.Blocks)
        {
            block.Runs ??= [];
            block.Level = block.Kind == BlockKind.Heading ? Math.Clamp(block.Level, 1, 3) : Math.Clamp(block.Level, 0, 8);
            foreach (var run in block.Runs)
            {
                run.Text ??= string.Empty;
                run.Style ??= RunStyle.Default;
                run.Style = run.Style with
                {
                    FontSize = run.Style.FontSize is { } size ? Math.Clamp(size, 4, 400) : null,
                    FontWeight = run.Style.FontWeight is { } weight ? Math.Clamp(weight, 1, 1000) : null,
                    LetterSpacing = run.Style.LetterSpacing is { } spacing ? Math.Clamp(spacing, -10, 50) : null
                };
            }
            if (block.Image is { } image)
            {
                image.Width = image.Width is { } width ? Math.Clamp(width, 16, 8192) : null;
                image.CornerRadius = Math.Clamp(image.CornerRadius, 0, 4096);
            }
            if (block.Kind == BlockKind.Image && (block.Image is null || !IsSafeAssetName(block.Image.Asset)))
            {
                block.Kind = BlockKind.Paragraph;
                block.Image = null;
            }
        }
        if (document.Blocks.Count == 0) document.Blocks.Add(NoteBlock.Paragraph());
    }
}
