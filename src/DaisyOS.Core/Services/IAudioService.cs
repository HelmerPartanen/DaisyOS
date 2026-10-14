using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IAudioService
{
    Task<double?> GetVolumeAsync(CancellationToken cancellationToken = default);

    Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default);

    Task<bool> GetMutedAsync(CancellationToken cancellationToken = default);

    Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default);

    Task<double?> GetInputVolumeAsync(CancellationToken cancellationToken = default);

    Task SetInputVolumeAsync(double volume, CancellationToken cancellationToken = default);

    Task<bool> GetInputMutedAsync(CancellationToken cancellationToken = default);

    Task SetInputMutedAsync(bool muted, CancellationToken cancellationToken = default);

    Task<string> GetDefaultDeviceNameAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AudioDeviceInfo>> GetAudioDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns false when PipeWire rejects the requested sink.</summary>
    Task<bool> SetDefaultAudioDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AudioDeviceInfo>> GetAudioInputDevicesAsync(CancellationToken cancellationToken = default);

    Task<bool> SetDefaultAudioInputDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AudioAppStreamInfo>> GetAudioAppStreamsAsync(CancellationToken cancellationToken = default);

    Task SetAppStreamVolumeAsync(string streamId, double volume, CancellationToken cancellationToken = default);
}
