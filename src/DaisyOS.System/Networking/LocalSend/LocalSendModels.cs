using System.Collections.ObjectModel;

namespace DaisyOS.System.Networking.LocalSend;

public sealed record LocalSendDevice(
    string Id,
    string Alias,
    string Address,
    int Port,
    string DeviceType,
    string Fingerprint,
    DateTimeOffset LastSeen);

/// <summary>
/// Transfer metadata supplied before consent. PreviewImage is optional and is
/// only used when a peer explicitly includes a safe inline image preview.
/// </summary>
public sealed record LocalSendFile(string Id, string Name, long Size, string? Sha256, string? PreviewImage = null);

public sealed record LocalSendIncomingTransfer(
    string Id,
    LocalSendDevice Sender,
    IReadOnlyList<LocalSendFile> Files,
    long TotalBytes,
    DateTimeOffset ExpiresAt)
{
    public string Summary => $"{Files.Count} file{(Files.Count == 1 ? string.Empty : "s")} · {FormatSize(TotalBytes)}";

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024d * 1024d):F1} MB",
        _ => $"{bytes / (1024d * 1024d * 1024d):F1} GB"
    };
}

public sealed record LocalSendTransferProgress(
    string TransferId,
    string DeviceName,
    bool IsIncoming,
    long CompletedBytes,
    long TotalBytes,
    string Status,
    string? DestinationPath = null);

/// <summary>Secure, LocalSend-v2.2-compatible LAN transfer capability.</summary>
public interface ILocalSendService : IAsyncDisposable
{
    event EventHandler? DevicesChanged;
    event EventHandler<LocalSendIncomingTransfer>? IncomingTransferRequested;
    event EventHandler<LocalSendTransferProgress>? TransferProgressChanged;
    event EventHandler? ReceivingStateChanged;

    bool IsRunning { get; }
    bool IsReceivingEnabled { get; }
    DateTimeOffset? ReceivingUntil { get; }
    IReadOnlyList<LocalSendDevice> GetDevices();
    IReadOnlyList<LocalSendIncomingTransfer> GetPendingTransfers();
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    void EnableReceivingFor(TimeSpan duration);
    void DisableReceiving();
    void RequestDiscoveryRefresh(bool forceSubnetProbe = false);
    Task<bool> DecideIncomingAsync(string transferId, bool accept, CancellationToken cancellationToken = default);
    Task SendAsync(LocalSendDevice device, IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
}
