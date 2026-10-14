using DaisyOS.Installer;
using Xunit;

namespace DaisyOS.Tests;

public sealed class InstallerViewModelTests
{
    [Fact]
    public void WizardRequiresSafetyAcknowledgementAndValidAccountDetails()
    {
        using var viewModel = new InstallerViewModel(new RecordingInstallerBackend());

        Assert.Equal(InstallerStep.Welcome, viewModel.Step);
        Assert.False(viewModel.CanGoNext);
        viewModel.Next();
        Assert.Equal(InstallerStep.Welcome, viewModel.Step);

        viewModel.VmWarningAccepted = true;
        viewModel.Next();
        Assert.Equal(InstallerStep.Region, viewModel.Step);
        viewModel.Next();
        Assert.Equal(InstallerStep.User, viewModel.Step);

        viewModel.FullName = "Alex Example";
        Assert.Equal("alex-example", viewModel.UserName);
        viewModel.Password = "different-one";
        viewModel.PasswordConfirmation = "different-two";
        Assert.False(viewModel.CanGoNext);
        viewModel.Next();
        Assert.Contains("match", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);

        viewModel.PasswordConfirmation = "different-one";
        Assert.True(viewModel.CanGoNext);
    }

    [Fact]
    public async Task MockInstallationRequiresExactPhraseAndCompletesWithoutDiskCapability()
    {
        var backend = new RecordingInstallerBackend();
        using var viewModel = CreateConfirmedViewModel(backend);

        Assert.Equal(InstallerStep.Confirm, viewModel.Step);
        viewModel.ConfirmationText = "install daisyos";
        Assert.False(viewModel.CanInstall);
        viewModel.ConfirmationText = InstallerViewModel.RequiredConfirmation;
        Assert.True(viewModel.CanInstall);

        await viewModel.InstallAsync();

        Assert.Equal(InstallerStep.Complete, viewModel.Step);
        Assert.Equal(1, backend.InstallCount);
        Assert.NotNull(backend.LastPlan);
        Assert.Equal("alex-example", backend.LastPlan!.UserName);
    }

    [Fact]
    public void BackendWithDiskCapabilityIsBlockedByPrototypeViewModel()
    {
        using var viewModel = CreateConfirmedViewModel(new RecordingInstallerBackend(canModifyDisks: true));
        viewModel.ConfirmationText = InstallerViewModel.RequiredConfirmation;
        Assert.False(viewModel.CanInstall);
    }

    private static InstallerViewModel CreateConfirmedViewModel(IInstallerBackend backend)
    {
        var viewModel = new InstallerViewModel(backend) { VmWarningAccepted = true };
        viewModel.Next();
        viewModel.Next();
        viewModel.FullName = "Alex Example";
        viewModel.Password = "safe-demo-password";
        viewModel.PasswordConfirmation = "safe-demo-password";
        viewModel.Next();
        viewModel.Next();
        viewModel.Next();
        Assert.Equal(InstallerStep.Confirm, viewModel.Step);
        return viewModel;
    }

    private sealed class RecordingInstallerBackend(bool canModifyDisks = false) : IInstallerBackend
    {
        public bool CanModifyDisks { get; } = canModifyDisks;
        public int InstallCount { get; private set; }
        public InstallerPlan? LastPlan { get; private set; }

        public Task InstallAsync(InstallerPlan plan, IProgress<InstallerProgress> progress, CancellationToken cancellationToken)
        {
            InstallCount++;
            LastPlan = plan;
            progress.Report(new InstallerProgress(100, "Complete", "Mock installation complete."));
            return Task.CompletedTask;
        }
    }
}
