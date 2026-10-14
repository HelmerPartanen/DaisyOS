namespace DaisyOS.Core.Models;

public sealed record GamingStatus(
    bool SteamInstalled,
    bool GameModeInstalled,
    bool MangoHudInstalled,
    bool GamescopeInstalled,
    bool WineInstalled,
    bool VulkanAvailable,
    bool Vulkan32Available,
    string? VulkanDriver,
    string GpuVendor,
    string GpuModel,
    bool ControllerRulesInstalled,
    bool BluetoothServiceAvailable,
    bool BluetoothServiceRunning,
    string Detail);
