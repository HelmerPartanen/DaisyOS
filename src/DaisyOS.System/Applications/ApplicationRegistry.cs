using System.Text.Json;
using DaisyOS.Core.Models;
using DaisyOS.System.Serialization;
namespace DaisyOS.System.Applications;

public sealed class ApplicationRegistry
{
    public string Root { get; }
    private string FilePath => Path.Combine(Root, "installations.json");
    public ApplicationRegistry(string? dataHome = null) => Root = Path.Combine(dataHome ??
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"), "daisyos", "applications");
    public List<InstalledApplication> Read()
    {
        if (!File.Exists(FilePath)) return [];
        // Corruption must not erase ownership records or enable unsafe cleanup.
        var records = JsonSerializer.Deserialize(File.ReadAllText(FilePath), DaisySystemJsonContext.Default.ListInstalledApplication)
            ?? throw new IOException("The application registry is invalid. Restore its backup before continuing.");
        return records.Select(Normalize).ToList();
    }
    public static InstalledApplication Normalize(InstalledApplication app)
    {
        if (app.Identity is null || string.IsNullOrWhiteSpace(app.Identity.PackageId)) throw new IOException("Invalid application identity in ownership records.");
        return app with { SourceFingerprint = app.SourceFingerprint ?? "", SourcePath = app.SourcePath ?? "", Origin = app.Origin ?? "", Architecture = app.Architecture ?? "", Image = app.Image ?? "",
            ImageDigest = app.ImageDigest ?? "", DataDirectory = app.DataDirectory ?? "", DesktopIds = app.DesktopIds ?? [], OwnedLaunchers = app.OwnedLaunchers ?? [],
            OwnedCommands = app.OwnedCommands ?? [], OwnedIntegrationFiles = app.OwnedIntegrationFiles ?? [], OwnedIcons = app.OwnedIcons ?? [], ReviewedPaths = app.ReviewedPaths ?? [], ProtectionReason = app.ProtectionReason ?? "" };
    }
    public void Save(InstalledApplication application)
    {
        var records = Read(); records.RemoveAll(record => record.Identity == application.Identity); records.Add(application); Write(records);
    }
    public void Remove(ApplicationIdentity identity) { var records = Read(); if (records.RemoveAll(record => record.Identity == identity) > 0) Write(records); }
    private void Write(List<InstalledApplication> records)
    {
        Directory.CreateDirectory(Root);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(records, DaisySystemJsonContext.Default.ListInstalledApplication));
            if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static bool HasSymbolicAncestor(string path)
    {
        for (FileSystemInfo? current = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path); current is not null; current = current is DirectoryInfo directory ? directory.Parent : ((FileInfo)current).Directory)
            if (current.LinkTarget is not null) return true;
        return false;
    }
    public static bool IsWithin(string path, string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal);
    }
}
