namespace DaisyOS.Core.Models;

public enum AirPodsNoiseMode { Off, Anc, Transparency, Adaptive }

public sealed record AirPodsStatus(bool IsAvailable, bool IsConnected, string Address, string Name,
    AirPodsNoiseMode? NoiseMode, IReadOnlyList<AirPodsNoiseMode> SupportedModes, string Battery, string NearbyAddress = "")
{
    public static AirPodsStatus Unavailable { get; } = new(false, false, "", "", null, [], "");
}
