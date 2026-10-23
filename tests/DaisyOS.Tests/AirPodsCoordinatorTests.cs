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

    [Fact]
    public void ConnectionAnnouncementRequiresExactConnectedAirPodsDevice()
    {
        var status = new AirPodsStatus(true, true, "AA:BB:CC:DD:EE:21", "Stale name", null, [], "", "");
        var mouse = new BluetoothDevice("AA:BB:CC:DD:EE:22", "M1", true, true, "Mouse", true);
        var disconnectedPods = new BluetoothDevice(status.Address, "saaga", false, true, "AirPods", true);
        var connectedPods = disconnectedPods with { IsConnected = true };

        Assert.Null(AirPodsCoordinator.FindConfirmedConnectedAirPods(status, [mouse, disconnectedPods]));
        Assert.Equal(connectedPods, AirPodsCoordinator.FindConfirmedConnectedAirPods(status, [mouse, connectedPods]));
    }

    [Fact]
    public void CachedBlueZRssiDoesNotCountAsACurrentAirPodsAdvertisement()
    {
        var cached = new BluetoothDevice("AA:BB:CC:DD:EE:21", "saaga", false, false, "AirPods", true);

        Assert.False(AirPodsCoordinator.HasCurrentAdvertisement(cached, AirPodsStatus.Unavailable));
        Assert.False(AirPodsCoordinator.HasCurrentAdvertisement(cached,
            AirPodsStatus.Unavailable with { NearbyAddress = "AA:BB:CC:DD:EE:22" }));
        Assert.True(AirPodsCoordinator.HasCurrentAdvertisement(cached,
            AirPodsStatus.Unavailable with { NearbyAddress = cached.Address }));
    }
}
