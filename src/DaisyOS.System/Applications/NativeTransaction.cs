namespace DaisyOS.System.Applications;
public sealed record NativeTransaction
{
    public string[] Packages { get; set; } = [];
    public string[] DesktopFiles { get; set; } = [];
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string Error { get; set; } = "";
    public bool Protected { get; set; }
    public long DownloadBytes { get; set; }
    public long InstalledBytes { get; set; }
}
