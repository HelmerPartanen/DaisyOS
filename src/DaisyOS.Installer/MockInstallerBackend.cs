namespace DaisyOS.Installer;

public sealed class MockInstallerBackend : IInstallerBackend
{
    private static readonly InstallerProgress[] Stages =
    [
        new(8, "Preparing installation", "Checking the mock installation plan."),
        new(22, "Preparing storage", "Simulating partition and encryption setup."),
        new(48, "Copying DaisyOS", "Simulating system file installation."),
        new(68, "Creating your account", "Simulating user and locale configuration."),
        new(84, "Configuring startup", "Simulating bootloader and recovery setup."),
        new(100, "Installation complete", "No disks were changed. This was a safe demonstration.")
    ];

    public bool CanModifyDisks => false;

    public async Task InstallAsync(
        InstallerPlan plan,
        IProgress<InstallerProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(progress);

        foreach (var stage in Stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(stage);
            await Task.Delay(180, cancellationToken);
        }
    }
}
