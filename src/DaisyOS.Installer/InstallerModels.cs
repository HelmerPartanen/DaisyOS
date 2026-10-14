namespace DaisyOS.Installer;

public enum InstallerStep
{
    Welcome,
    Region,
    User,
    Disk,
    Summary,
    Confirm,
    Installing,
    Complete
}

public sealed record InstallerDisk(string Id, string Name, string Size, string Detail)
{
    public string DisplayName => $"{Name} · {Size}";
}

public sealed record InstallerPlan(
    string Language,
    string Keyboard,
    string TimeZone,
    string FullName,
    string UserName,
    string HostName,
    InstallerDisk Disk,
    bool Encrypt,
    bool CreateRecoveryPartition);

public sealed record InstallerProgress(int Percentage, string Stage, string Detail);

public interface IInstallerBackend
{
    bool CanModifyDisks { get; }
    Task InstallAsync(InstallerPlan plan, IProgress<InstallerProgress> progress, CancellationToken cancellationToken);
}
