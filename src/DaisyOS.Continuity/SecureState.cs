using global::System.Runtime.InteropServices;
using global::System.Security.Cryptography;
using global::System.Security.Cryptography.X509Certificates;
using global::System.Text.Json;

namespace DaisyOS.Continuity;

public sealed record TrustedPeer(string Certificate, string Name, string Address, bool Clipboard = false, bool Files = false, string GrantId = "", bool Input = false, bool Handoff = false, bool Screen = false, bool Audio = false, int BlockedFeatures = 0, string InputEdge = "right", long EdgeRevision = 0, string EdgeAuthor = "");
public sealed class ContinuitySettings
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; }
    public bool Paused { get; set; }
    public bool Visible { get; set; }
    public string Name { get; set; } = Environment.MachineName;
    public Dictionary<string, TrustedPeer> Peers { get; set; } = new(StringComparer.Ordinal);
}

public sealed partial class SecureState : IDisposable
{
    public string DirectoryPath { get; }
    public X509Certificate2 Identity { get; }
    public string Id => Fingerprint(Identity.RawData);
    public ContinuitySettings Settings { get; }
    private readonly FileStream _lock;
    public SecureState(string directory)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        DirectoryPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Check(DirectoryPath, true);
        var probe = Path.Combine(DirectoryPath, ".write-probe-" + Guid.NewGuid().ToString("N"));
        try { WritePrivate(probe, new byte[] { 1 }); }
        finally { File.Delete(probe); }
        SyncDirectory(DirectoryPath);
        var lockPath = Path.Combine(DirectoryPath, "daemon.lock");
        if (File.Exists(lockPath)) Check(lockPath, false);
        _lock = new FileStream(lockPath, new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite,
            Share = FileShare.None, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        _lock.Lock(0, 1);
        var identityPath = Path.Combine(DirectoryPath, "identity.pfx");
        if (File.Exists(identityPath))
        {
            Check(identityPath, false);
            Identity = X509CertificateLoader.LoadPkcs12FromFile(identityPath, null, X509KeyStorageFlags.EphemeralKeySet);
        }
        else
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=daisycont.local", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
            var usages = new OidCollection { new("1.3.6.1.5.5.7.3.1"), new("1.3.6.1.5.5.7.3.2") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("daisycont.local");
            request.CertificateExtensions.Add(san.Build());
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
            var bytes = cert.Export(X509ContentType.Pfx);
            WritePrivate(identityPath, bytes);
            CryptographicOperations.ZeroMemory(bytes);
            Identity = X509CertificateLoader.LoadPkcs12FromFile(identityPath, null, X509KeyStorageFlags.EphemeralKeySet);
        }
        if (!Identity.HasPrivateKey) throw new IOException("Missing private identity key.");
        var settingsPath = Path.Combine(DirectoryPath, "settings.json");
        if (File.Exists(settingsPath))
        {
            Check(settingsPath, false);
            using (File.Open(settingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
            if (new FileInfo(settingsPath).Length > 1024 * 1024) throw new IOException("State limit exceeded.");
            Settings = JsonSerializer.Deserialize<ContinuitySettings>(File.ReadAllBytes(settingsPath)) ?? throw new IOException("Invalid state.");
            if (Settings.Version != 1 || Settings.Peers.Count > 64) throw new IOException("Unsupported state version or peer limit.");
            foreach (var (id, peer) in Settings.Peers)
                if (Fingerprint(Convert.FromBase64String(peer.Certificate)) != id) throw new IOException("Invalid peer pin.");
        }
        else Settings = new();
        // The simplified UI has one on/off control and no separate pause mode.
        Settings.Paused = false;
        if (Settings.Enabled) Settings.Visible = true;
        // Pairing now grants the full Continuity experience, including old pairs.
        foreach (var (id, peer) in Settings.Peers.ToArray())
            Settings.Peers[id] = peer with { Clipboard = true, Files = true, Input = true, Handoff = true, Screen = true, Audio = true, BlockedFeatures = 0 };
    }
    public void Save()
    {
        var path = Path.Combine(DirectoryPath, "settings.json");
        try { WritePrivate(path, JsonSerializer.SerializeToUtf8Bytes(Settings)); }
        catch
        {
            // A failed security mutation must not leave an enabled old trust
            // database to be silently reused after a normal restart.
            Settings.Enabled = false;
            try { File.Delete(path); SyncDirectory(DirectoryPath); } catch { }
            throw;
        }
    }
    public static string Fingerprint(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string SafeName(string value) => new(value.Where(c => !char.IsControl(c) && c is not (>= '\u202a' and <= '\u202e') && c is not (>= '\u2066' and <= '\u2069')).Take(80).ToArray());
    public static void WritePrivate(string path, byte[] data)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temporary, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
            { file.Write(data); file.Flush(true); }
            File.Move(temporary, path, true);
            SyncDirectory(Path.GetDirectoryName(path)!);
        }
        finally { File.Delete(temporary); }
    }
    public static void SyncDirectory(string path)
    {
        var fd = open(path, 0x10000 | 0x80000); // O_DIRECTORY | O_CLOEXEC, read-only.
        if (fd < 0) throw new IOException("Cannot open private state directory.");
        try { if (fsync(fd) != 0) throw new IOException("Cannot persist private state directory."); }
        finally { close(fd); }
    }
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int open(string path, int flags);
    [LibraryImport("libc", SetLastError = true)] private static partial int fsync(int fd);
    [LibraryImport("libc")] private static partial int close(int fd);
    public static bool IsRegularFile(string path) => statx(-100, path, 0, 0x7ff, out var info) == 0 && (info.Mode & 0xf000) == 0x8000;
    public static void Check(string path, bool directory)
    {
        if (statx(-100, path, 0x100, 0x7ff, out var info) != 0 || info.Uid != geteuid() ||
            (info.Mode & 0xf000) != (directory ? 0x4000 : 0x8000) || (info.Mode & 0x3f) != 0)
            throw new IOException("Continuity state must be private, user-owned, and not a symlink.");
    }
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Statx { [FieldOffset(20)] public uint Uid; [FieldOffset(28)] public ushort Mode; }
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int statx(int fd, string path, int flags, uint mask, out Statx result);
    [LibraryImport("libc")] private static partial uint geteuid();
    public void Dispose() { Identity.Dispose(); _lock.Dispose(); }
}
