using DaisyOS.Core.Models;
using DaisyOS.Shell.Services;
using Xunit;

namespace DaisyOS.Tests;

public sealed class AirPodsCoordinatorTests
{
    [Fact]
    public void PairingPromptIsOnlyOfferedForUnpairedNearbyNamedDevices()
    {
        var detected = new BluetoothDevice("AA:BB:CC:DD:EE:21", "AirPods Pro", false, false, "AirPods", true);

        Assert.True(AirPodsCoordinator.ShouldOfferPairingPrompt(detected));
        Assert.False(AirPodsCoordinator.ShouldOfferPairingPrompt(detected with { IsPaired = true }));
        Assert.False(AirPodsCoordinator.ShouldOfferPairingPrompt(detected with { IsNearby = false }));
        Assert.False(AirPodsCoordinator.ShouldOfferPairingPrompt(detected with { Name = "AA-BB-CC-DD-EE-21" }));
    }
}
