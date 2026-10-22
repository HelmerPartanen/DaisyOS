using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DaisyOS.System.Networking.LocalSend;

public sealed class LocalSendService : ILocalSendService
{
    public const int DefaultPort = 53317;
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.167");
    private static readonly JsonSerializerOptions ProtocolJson = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private const int MaxPendingTransfers = 4;
    private const long MaxSingleFileBytes = 100L * 1024 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, LocalSendDevice> _devices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingTransfer> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AcceptedTransfer> _accepted = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _discoveryRequests = new(0, 1);
    private readonly string _downloadsDirectory;
    private readonly string _stagingDirectory;
    private readonly string _alias;
    private readonly X509Certificate2 _certificate;
    private readonly string _fingerprint;
    private DateTimeOffset _nextSubnetProbeAt = DateTimeOffset.MinValue;
    private WebApplication? _app;
    private CancellationTokenSource? _lifetime;
    private Task? _discoveryTask;
    private Timer? _receivingExpiryTimer;
    private DateTimeOffset? _receivingUntil;

    public LocalSendService(string? alias = null, string? downloadsDirectory = null)
    {
        _alias = string.IsNullOrWhiteSpace(alias) ? Environment.MachineName : alias.Trim();
        _downloadsDirectory = downloadsDirectory ?? ResolveDownloadsDirectory();
        _stagingDirectory = Path.Combine(_downloadsDirectory, ".daisyos-localsend");
        _certificate = LoadOrCreateCertificate();
        _fingerprint = Convert.ToHexString(SHA256.HashData(_certificate.RawData));
    }

    internal static string ResolveDownloadsDirectory(string? homePath = null, string? configPath = null) =>
        DaisyOS.Core.Storage.UserDirectoryPaths.Load(homePath, configPath)["Downloads"];

    public event EventHandler? DevicesChanged;
    public event EventHandler<LocalSendIncomingTransfer>? IncomingTransferRequested;
    public event EventHandler<LocalSendTransferProgress>? TransferProgressChanged;
    public event EventHandler? ReceivingStateChanged;
    public bool IsRunning => _app is not null;
    public bool IsReceivingEnabled
    {
        get
        {
            lock (_gate)
            {
                return _receivingUntil is { } until && until > DateTimeOffset.UtcNow;
            }
        }
    }

    public DateTimeOffset? ReceivingUntil
    {
        get
        {
            lock (_gate)
            {
                return _receivingUntil is { } until && until > DateTimeOffset.UtcNow ? until : null;
            }
        }
    }

    public IReadOnlyList<LocalSendDevice> GetDevices() => _devices.Values
        .OrderByDescending(device => device.LastSeen)
        .ToArray();

    public IReadOnlyList<LocalSendIncomingTransfer> GetPendingTransfers() => _pending.Values
        .Select(pending => pending.Transfer)
        .OrderBy(transfer => transfer.ExpiresAt)
        .ToArray();

    internal static WebApplicationBuilder CreateHostBuilder()
    {
        // This is an embedded protocol server, not a configurable website.
        // Default JSON reload watchers recursively watch the working directory
        // on Linux, including a development checkout's entire dependency tree.
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseKestrel();
        builder.Services.AddRouting();
        builder.Logging.AddSimpleConsole();
        return builder;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_app is not null) return;
            Directory.CreateDirectory(_downloadsDirectory);
            Directory.CreateDirectory(_stagingDirectory);
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        var builder = CreateHostBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(DefaultPort, listen => listen.UseHttps(new HttpsConnectionAdapterOptions
            {
                ServerCertificate = _certificate,
                ClientCertificateMode = ClientCertificateMode.AllowCertificate,
                ClientCertificateValidation = static (_, _, _) => true
            }));
        });
        var app = builder.Build();
        // Keep this as a route-handler lambda. Passing the method group as a
        // RequestDelegate makes ASP.NET discard its IResult response, leaving
        // LocalSend peers without the registration identity they expect.
        app.MapPost("/api/localsend/v2/register", async (HttpContext context, RegisterInfo info) =>
            await RegisterAsync(context, info).ConfigureAwait(false));
        app.MapGet("/api/localsend/v2/info", Info);
        app.MapPost("/api/localsend/v2/prepare-upload", PrepareUploadAsync);
        app.MapPost("/api/localsend/v2/upload", async (HttpContext context) =>
            await UploadAsync(context).ConfigureAwait(false));
        app.MapPost("/api/localsend/v2/cancel", CancelAsync);

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate) _app = app;
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            _discoveryTask = RunDiscoveryAsync(_lifetime!.Token);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            lock (_gate)
            {
                _lifetime?.Dispose();
                _lifetime = null;
            }
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        DisableReceiving();
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        WebApplication? app;
        CancellationTokenSource? lifetime;
        lock (_gate)
        {
            app = _app;
            _app = null;
            lifetime = _lifetime;
            _lifetime = null;
        }
        lifetime?.Cancel();
        if (_discoveryTask is not null)
        {
            try { await _discoveryTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _discoveryTask = null;
        }
        if (app is not null)
        {
            await app.StopAsync(cancellationToken).ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
        lifetime?.Dispose();
    }

    public void EnableReceivingFor(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "The LocalSend receive duration must be positive.");
        }

        lock (_gate)
        {
            _receivingUntil = DateTimeOffset.UtcNow.Add(duration);
            _receivingExpiryTimer?.Dispose();
            _receivingExpiryTimer = new Timer(
                static state => ((LocalSendService)state!).DisableReceiving(),
                this,
                duration,
                Timeout.InfiniteTimeSpan);
        }

        ReceivingStateChanged?.Invoke(this, EventArgs.Empty);
        // Wi-Fi access points commonly isolate multicast from wired clients.
        // Ask the discovery loop for an immediate multicast burst and its
        // authenticated local-subnet fallback instead of waiting for its next
        // periodic pass. That makes the receiving device appear on a phone
        // when the user explicitly turns receiving on.
        _nextSubnetProbeAt = DateTimeOffset.MinValue;
        RequestDiscoveryRefresh();
        Console.Error.WriteLine("[localsend] Receiving enabled for ten minutes; refreshing nearby devices.");
    }

    public void DisableReceiving()
    {
        PendingTransfer[] pendingTransfers;
        bool changed;
        lock (_gate)
        {
            changed = _receivingUntil is not null;
            _receivingUntil = null;
            _receivingExpiryTimer?.Dispose();
            _receivingExpiryTimer = null;
            pendingTransfers = _pending.Values.ToArray();
        }

        // A receiving window is a consent boundary. Closing it also declines
        // requests that have not yet received an explicit user decision.
        foreach (var pending in pendingTransfers)
        {
            pending.Decision.TrySetResult(null);
        }

        if (changed)
        {
            ReceivingStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<bool> DecideIncomingAsync(string transferId, bool accept, CancellationToken cancellationToken = default)
    {
        if (!_pending.TryRemove(transferId, out var pending)) return false;
        using var registration = cancellationToken.Register(() => pending.Decision.TrySetCanceled(cancellationToken));
        if (!accept)
        {
            pending.Decision.TrySetResult(null);
            return true;
        }

        // When the user explicitly accepts, ensure receiving is enabled so the incoming upload completes.
        EnableReceivingFor(TimeSpan.FromMinutes(10));

        var tokens = pending.Files.ToDictionary(file => file.Id, _ => Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
        var accepted = new AcceptedTransfer(pending.Transfer, tokens, pending.Files.ToDictionary(file => file.Id, StringComparer.Ordinal));
        _accepted[transferId] = accepted;
        pending.Decision.TrySetResult(accepted);
        return true;
    }

    public async Task SendAsync(LocalSendDevice device, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var files = paths.Where(File.Exists).Select(path => new SendFile(Guid.NewGuid().ToString("N"), path)).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("Choose at least one existing file to send.");
        if (files.Any(file => new FileInfo(file.Path).Length > MaxSingleFileBytes)) throw new InvalidOperationException("A selected file is too large for LocalSend.");

        var transferId = Guid.NewGuid().ToString("N");
        var payload = new PrepareUploadRequest(
            SelfInfo(),
            files.ToDictionary(file => file.Id, file => new FileInfoDto(file.Id, Path.GetFileName(file.Path), new FileInfo(file.Path).Length, GetMimeType(file.Path), null), StringComparer.Ordinal));
        using var client = CreateClient(device.Fingerprint);
        var endpoint = $"https://{FormatHost(device.Address)}:{device.Port}/api/localsend/v2";
        using var response = await client.PostAsJsonAsync($"{endpoint}/prepare-upload", payload, DaisyOS.System.Serialization.DaisySystemJsonContext.Default.PrepareUploadRequest, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var accepted = await response.Content.ReadFromJsonAsync(DaisyOS.System.Serialization.DaisySystemJsonContext.Default.PrepareUploadResponse, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The receiving device sent an invalid LocalSend response.");
        var total = files.Sum(file => new FileInfo(file.Path).Length);
        long sent = 0;
        foreach (var file in files)
        {
            if (!accepted.Files.TryGetValue(file.Id, out var token)) continue;
            await using var stream = File.OpenRead(file.Path);
            using var content = new StreamContent(stream);
            using var upload = await client.PostAsync($"{endpoint}/upload?sessionId={Uri.EscapeDataString(accepted.SessionId)}&fileId={Uri.EscapeDataString(file.Id)}&token={Uri.EscapeDataString(token)}", content, cancellationToken).ConfigureAwait(false);
            upload.EnsureSuccessStatusCode();
            sent += stream.Length;
            TransferProgressChanged?.Invoke(this, new LocalSendTransferProgress(transferId, device.Alias, false, sent, total, "Completed"));
        }
    }

    private async Task<IResult> RegisterAsync(HttpContext context, RegisterInfo info)
    {
        var device = await TrackDeviceAsync(context, info).ConfigureAwait(false);
        return Results.Json(new RegisterResponse(_alias, "2.2", "DaisyOS", "desktop", _fingerprint, false), DaisyOS.System.Serialization.DaisySystemJsonContext.Default.RegisterResponse);
    }

    private IResult Info() => Results.Json(new RegisterResponse(_alias, "2.2", "DaisyOS", "desktop", _fingerprint, false), DaisyOS.System.Serialization.DaisySystemJsonContext.Default.RegisterResponse);

    private async Task<IResult> PrepareUploadAsync(HttpContext context, PrepareUploadRequest request)
    {
        if (!IsReceivingEnabled)
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (_pending.Count >= MaxPendingTransfers || request.Files.Count == 0 || request.Files.Values.Any(file => !IsSafeFile(file)))
            return Results.Json(new LocalSendErrorMessage("Invalid or unsupported LocalSend upload request."), DaisyOS.System.Serialization.DaisySystemJsonContext.Default.LocalSendErrorMessage, statusCode: StatusCodes.Status400BadRequest);
        var sender = await TrackDeviceAsync(context, request.Info).ConfigureAwait(false);
        var transfer = new LocalSendIncomingTransfer(
            Guid.NewGuid().ToString("N"),
            sender,
            request.Files.Values.Select(file => new LocalSendFile(file.Id, file.FileName, file.Size, file.Sha256, file.Preview)).ToArray(),
            request.Files.Values.Sum(file => file.Size),
            DateTimeOffset.UtcNow.AddMinutes(2));
        var pending = new PendingTransfer(transfer, request.Files.Values.ToArray());
        if (!_pending.TryAdd(transfer.Id, pending)) return Results.Json(new LocalSendErrorMessage("Another LocalSend transfer is pending."), DaisyOS.System.Serialization.DaisySystemJsonContext.Default.LocalSendErrorMessage, statusCode: StatusCodes.Status409Conflict);
        IncomingTransferRequested?.Invoke(this, transfer);
        AcceptedTransfer? decision;
        try { decision = await pending.Decision.Task.WaitAsync(TimeSpan.FromMinutes(2), context.RequestAborted).ConfigureAwait(false); }
        catch (TimeoutException) { _pending.TryRemove(transfer.Id, out _); return Results.StatusCode(StatusCodes.Status408RequestTimeout); }
        catch (OperationCanceledException) { _pending.TryRemove(transfer.Id, out _); return Results.StatusCode(StatusCodes.Status403Forbidden); }
        if (decision is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        return Results.Json(new PrepareUploadResponse(transfer.Id, decision.Tokens), DaisyOS.System.Serialization.DaisySystemJsonContext.Default.PrepareUploadResponse);
    }

    private async Task<IResult> UploadAsync(HttpContext context)
    {
        var sessionId = context.Request.Query["sessionId"].ToString();
        var fileId = context.Request.Query["fileId"].ToString();
        var token = context.Request.Query["token"].ToString();
        if (!_accepted.TryGetValue(sessionId, out var transfer) || !transfer.Tokens.TryGetValue(fileId, out var expectedToken) || !CryptographicOperations.FixedTimeEquals(global::System.Text.Encoding.UTF8.GetBytes(token), global::System.Text.Encoding.UTF8.GetBytes(expectedToken)))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!transfer.Files.TryGetValue(fileId, out var file)) return Results.BadRequest();

        Directory.CreateDirectory(_stagingDirectory);
        Directory.CreateDirectory(_downloadsDirectory);

        var temporary = Path.Combine(_stagingDirectory, $"{sessionId}-{fileId}.part");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous))
            {
                await context.Request.Body.CopyToAsync(output, context.RequestAborted).ConfigureAwait(false);
                if (output.Length != file.Size) throw new InvalidDataException($"The uploaded file size ({output.Length}) did not match the LocalSend request ({file.Size}).");
                await output.FlushAsync(context.RequestAborted).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(file.Sha256))
            {
                await using var input = File.OpenRead(temporary);
                var computedHash = Convert.ToHexString(await SHA256.HashDataAsync(input, context.RequestAborted).ConfigureAwait(false));
                if (!string.Equals(computedHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"[localsend] SHA256 mismatch for {file.FileName}: expected {file.Sha256}, got {computedHash}");
                    if (File.Exists(temporary)) File.Delete(temporary);
                    return Results.StatusCode(StatusCodes.Status422UnprocessableEntity);
                }
            }

            var destination = GetAvailableDestination(file.FileName);
            File.Move(temporary, destination, overwrite: true);
            Console.WriteLine($"[localsend] Received file '{Path.GetFileName(destination)}' saved to {destination}");
            TransferProgressChanged?.Invoke(this, new LocalSendTransferProgress(sessionId, transfer.Transfer.Sender.Alias, true, file.Size, transfer.Transfer.TotalBytes, "Completed", destination));
            return Results.Ok();
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"[localsend] Invalid upload data: {ex.Message}");
            if (File.Exists(temporary)) File.Delete(temporary);
            return Results.BadRequest();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[localsend] Upload error: {ex.GetType().Name}: {ex.Message}");
            if (File.Exists(temporary)) File.Delete(temporary);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private IResult CancelAsync(HttpContext context)
    {
        var sessionId = context.Request.Query["sessionId"].ToString();
        _pending.TryRemove(sessionId, out var pending);
        pending?.Decision.TrySetResult(null);
        _accepted.TryRemove(sessionId, out _);
        return Results.Ok();
    }

    private async Task RunDiscoveryAsync(CancellationToken cancellationToken)
    {
        var announcement = JsonSerializer.SerializeToUtf8Bytes(
            new MulticastMessage(_alias, "2.2", "DaisyOS", "desktop", _fingerprint, DefaultPort, "https", false, true),
            DaisyOS.System.Serialization.DaisySystemJsonContext.Default.MulticastMessage);

        var activeSockets = new List<DiscoverySocket>();
        var activeReceivers = new List<Task>();
        var receiverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        string lastInterfaceFingerprint = string.Empty;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var currentInterfaces = GetActiveMulticastInterfaceInfo();
                var currentFingerprint = string.Join(";", currentInterfaces
                    .OrderBy(i => i.Name, StringComparer.Ordinal)
                    .ThenBy(i => i.Address.ToString(), StringComparer.Ordinal)
                    .Select(i => $"{i.Name}:{i.Address}"));

                if (currentFingerprint != lastInterfaceFingerprint)
                {
                    receiverCts.Cancel();
                    try { await Task.WhenAll(activeReceivers).ConfigureAwait(false); } catch { }
                    foreach (var socket in activeSockets) socket.Client.Dispose();
                    activeSockets.Clear();
                    activeReceivers.Clear();
                    receiverCts.Dispose();
                    receiverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                    activeSockets.AddRange(CreateDiscoverySockets());
                    lastInterfaceFingerprint = currentFingerprint;

                    if (activeSockets.Count > 0)
                    {
                        foreach (var socket in activeSockets)
                        {
                            activeReceivers.Add(ReceiveAnnouncementsAsync(socket, receiverCts.Token));
                        }
                        _nextSubnetProbeAt = DateTimeOffset.MinValue;
                    }
                    else
                    {
                        Console.Error.WriteLine("[localsend] No multicast-capable network interface is available yet; waiting for connection...");
                    }
                }

                if (activeSockets.Count == 0)
                {
                    await _discoveryRequests.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // LocalSend peers can miss an individual multicast packet when
                // waking from a mobile power-saving state. Send a compact
                // burst now, then refresh presence periodically.
                foreach (var delay in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(2) })
                {
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    foreach (var socket in activeSockets)
                    {
                        try
                        {
                            await socket.Client.SendAsync(announcement, announcement.Length, new IPEndPoint(MulticastAddress, DefaultPort)).ConfigureAwait(false);
                        }
                        catch (SocketException ex)
                        {
                            Console.Error.WriteLine($"[localsend] Multicast send failed on {socket.InterfaceName}: {ex.Message}");
                        }
                    }
                }

                // Some access points deliberately suppress multicast between
                // Wi-Fi and Ethernet clients. Fall back to the single
                // LocalSend port on the current private subnet, never to a
                // general port scan. Every candidate is still authenticated
                // against its advertised TLS fingerprint before it is shown.
                await DiscoverPeersByLocalSubnetAsync(activeSockets, announcement, cancellationToken).ConfigureAwait(false);
                await _discoveryRequests.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shell shutdown.
        }
        finally
        {
            receiverCts.Cancel();
            foreach (var socket in activeSockets) socket.Client.Dispose();
            try { await Task.WhenAll(activeReceivers).ConfigureAwait(false); } catch { }
            receiverCts.Dispose();
        }
    }

    private static List<(string Name, IPAddress Address)> GetActiveMulticastInterfaceInfo()
    {
        var list = new List<(string Name, IPAddress Address)>();
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up ||
                    network.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    !network.SupportsMulticast)
                {
                    continue;
                }

                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) continue;
                    list.Add((network.Name, address));
                }
            }
        }
        catch
        {
            // Network stack may throw transient exceptions during interface transitions
        }
        return list;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        Console.Error.WriteLine("[localsend] Network address changed; refreshing discovery.");
        RequestDiscoveryRefresh(forceSubnetProbe: true);
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        Console.Error.WriteLine($"[localsend] Network availability changed (IsAvailable={e.IsAvailable}); refreshing discovery.");
        if (e.IsAvailable)
        {
            RequestDiscoveryRefresh(forceSubnetProbe: true);
        }
    }

    private List<DiscoverySocket> CreateDiscoverySockets()
    {
        var sockets = new List<DiscoverySocket>();
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                !network.SupportsMulticast)
            {
                continue;
            }

            foreach (var unicast in network.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) continue;
                try
                {
                    var client = new UdpClient(AddressFamily.InterNetwork);
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    client.Client.Bind(new IPEndPoint(IPAddress.Any, DefaultPort));
                    client.JoinMulticastGroup(MulticastAddress, address);
                    // A join controls incoming traffic only. Pin outgoing
                    // multicast to this interface so wired, Wi-Fi, VPN and
                    // virtual adapters never all announce through the route
                    // selected by the OS.
                    client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                    client.Ttl = 1;
                    client.MulticastLoopback = true;
                    client.EnableBroadcast = true;
                    sockets.Add(new DiscoverySocket(client, network.Name, address));
                    Console.Error.WriteLine($"[localsend] Discovery listening on {network.Name} ({address}).");
                }
                catch (SocketException ex)
                {
                    Console.Error.WriteLine($"[localsend] Could not use {network.Name} ({address}) for discovery: {ex.Message}");
                }
            }
        }
        return sockets;
    }

    private async Task ReceiveAnnouncementsAsync(DiscoverySocket socket, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var received = await socket.Client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var message = JsonSerializer.Deserialize(received.Buffer, DaisyOS.System.Serialization.DaisySystemJsonContext.Default.MulticastMessage);
                if (message is null || message.Fingerprint == _fingerprint || !message.Announce) continue;
                _ = RegisterPeerAsync(received.RemoteEndPoint.Address, message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"[localsend] Discovery receive failed on {socket.InterfaceName}: {ex.Message}");
        }
    }

    public void RequestDiscoveryRefresh(bool forceSubnetProbe = false)
    {
        if (forceSubnetProbe)
        {
            _nextSubnetProbeAt = DateTimeOffset.MinValue;
        }

        try
        {
            if (_discoveryRequests.CurrentCount == 0)
            {
                _discoveryRequests.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Another request is already queued; one fresh pass is enough.
        }
    }

    private async Task DiscoverPeersByLocalSubnetAsync(
        IEnumerable<DiscoverySocket> sockets,
        byte[] announcement,
        CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _nextSubnetProbeAt) return;
        _nextSubnetProbeAt = DateTimeOffset.UtcNow.Add(_devices.IsEmpty ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(2));

        foreach (var socket in sockets)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var localIp = socket.Address;
            var network = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(nic => string.Equals(nic.Name, socket.InterfaceName, StringComparison.Ordinal));
            var unicast = network?.GetIPProperties().UnicastAddresses
                .FirstOrDefault(ip => ip.Address.Equals(localIp));
            if (!IsPrivate(localIp)) continue;

            uint maskUint = 0;
            if (unicast?.IPv4Mask is not null)
            {
                maskUint = ToUInt32(unicast.IPv4Mask);
            }
            if (maskUint == 0 && unicast is not null && unicast.PrefixLength is >= 8 and <= 30)
            {
                maskUint = uint.MaxValue << (32 - unicast.PrefixLength);
            }
            if (maskUint == 0)
            {
                maskUint = 0xFFFFFF00;
            }

            var localUint = ToUInt32(localIp);
            var hostCount = ~maskUint;
            if (hostCount is 0 or > 1024) continue;

            var networkUint = localUint & maskUint;

            // Send to directed subnet broadcast address first (e.g. 192.168.10.255)
            var broadcastUint = networkUint | hostCount;
            var broadcastIp = FromUInt32(broadcastUint);
            try
            {
                await socket.Client.SendAsync(announcement, announcement.Length, new IPEndPoint(broadcastIp, DefaultPort)).ConfigureAwait(false);
            }
            catch (SocketException) { }

            // Unicast sweep for APs that suppress broadcast
            for (uint host = 1; host < hostCount; host++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var targetUint = networkUint | host;
                if (targetUint == localUint) continue;
                var targetIp = FromUInt32(targetUint);

                try
                {
                    await socket.Client.SendAsync(announcement, announcement.Length, new IPEndPoint(targetIp, DefaultPort)).ConfigureAwait(false);
                }
                catch (SocketException) { }
            }
        }
    }

    private static async Task<LocalSendDevice?> ProbeCandidateAsync(
        IPAddress address,
        string? certificateFingerprint,
        CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(500),
            SslOptions = new global::System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null
            }
        };
        using var client = new HttpClient(handler);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        try
        {
            using var response = await client.GetAsync($"https://{FormatHost(address.ToString())}:{DefaultPort}/api/localsend/v2/info", timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var info = await response.Content.ReadFromJsonAsync(DaisyOS.System.Serialization.DaisySystemJsonContext.Default.RegisterResponse, timeout.Token).ConfigureAwait(false);
            if (info is null || string.IsNullOrWhiteSpace(certificateFingerprint) ||
                !string.Equals(info.Fingerprint, certificateFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return new LocalSendDevice(info.Fingerprint, info.Alias, address.ToString(), DefaultPort, info.DeviceType ?? "desktop", info.Fingerprint, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
    }

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) => new(new byte[]
    {
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
    });

    private async Task RegisterPeerAsync(IPAddress address, MulticastMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(message.Fingerprint);
            using var response = await client.PostAsJsonAsync($"https://{FormatHost(address.ToString())}:{message.Port}/api/localsend/v2/register", SelfInfo(), DaisyOS.System.Serialization.DaisySystemJsonContext.Default.RegisterInfo, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;
            var peer = new LocalSendDevice(message.Fingerprint, message.Alias, address.ToString(), message.Port, message.DeviceType ?? "desktop", message.Fingerprint, DateTimeOffset.UtcNow);
            _devices[peer.Id] = peer;
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch { /* Multicast peers regularly disappear; the next announcement retries safely. */ }
    }

    private async Task RegisterWithPeerAsync(LocalSendDevice peer, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(peer.Fingerprint);
            using var response = await client.PostAsJsonAsync(
                $"https://{FormatHost(peer.Address)}:{peer.Port}/api/localsend/v2/register",
                SelfInfo(),
                DaisyOS.System.Serialization.DaisySystemJsonContext.Default.RegisterInfo,
                cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[localsend] Registered DaisyOS with {peer.Alias} through local-network fallback discovery.");
            }
            else
            {
                Console.Error.WriteLine($"[localsend] {peer.Alias} rejected LocalSend registration ({(int)response.StatusCode}).");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine($"[localsend] Could not register with {peer.Alias}: {ex.Message}");
        }
    }

    private async Task<LocalSendDevice> TrackDeviceAsync(HttpContext context, RegisterInfo info)
    {
        var certificate = await context.Connection.GetClientCertificateAsync(context.RequestAborted).ConfigureAwait(false);
        var fingerprint = certificate is null ? info.Fingerprint : Convert.ToHexString(SHA256.HashData(certificate.RawData));
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var device = new LocalSendDevice(fingerprint, info.Alias, address, info.Port, info.DeviceType ?? "desktop", fingerprint, DateTimeOffset.UtcNow);
        _devices[device.Id] = device;
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        return device;
    }

    private HttpClient CreateClient(string fingerprint)
    {
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(_certificate);
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null && string.Equals(Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())), fingerprint, StringComparison.OrdinalIgnoreCase);
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
    }

    private RegisterInfo SelfInfo() => new(_alias, "2.2", "DaisyOS", "desktop", _fingerprint, DefaultPort, "https", false);
    private static string FormatHost(string host) => host.Contains(':') ? $"[{host}]" : host;
    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".pdf" => "application/pdf", _ => "application/octet-stream" };
    private static bool IsSafeFile(FileInfoDto file) =>
        !string.IsNullOrWhiteSpace(file.Id)
        && file.Size is >= 0 and <= MaxSingleFileBytes
        && !string.IsNullOrWhiteSpace(file.FileName)
        && file.FileName is not "." and not "..";

    private string GetAvailableDestination(string name)
    {
        var sanitizedName = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(sanitizedName) || sanitizedName is "." or "..")
        {
            sanitizedName = $"localsend-{DateTime.Now:yyyyMMdd-HHmmss}";
        }

        Directory.CreateDirectory(_downloadsDirectory);
        var basePath = Path.Combine(_downloadsDirectory, sanitizedName);
        if (!File.Exists(basePath)) return basePath;

        var stem = Path.GetFileNameWithoutExtension(sanitizedName);
        var extension = Path.GetExtension(sanitizedName);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(_downloadsDirectory, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private X509Certificate2 LoadOrCreateCertificate()
    {
        var identityDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DaisyOS", "LocalSend");
        Directory.CreateDirectory(identityDirectory);
        var pfx = Path.Combine(identityDirectory, "identity.pfx");
        const string password = "DaisyOS-LocalSend-v1";
        if (File.Exists(pfx)) return new X509Certificate2(pfx, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={_alias}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        File.WriteAllBytes(pfx, certificate.Export(X509ContentType.Pfx, password));
        try { File.SetUnixFileMode(pfx, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch (PlatformNotSupportedException) { }
        return new X509Certificate2(pfx, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    internal sealed record PendingTransfer(LocalSendIncomingTransfer Transfer, FileInfoDto[] Files) { public TaskCompletionSource<AcceptedTransfer?> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    internal sealed record AcceptedTransfer(LocalSendIncomingTransfer Transfer, Dictionary<string, string> Tokens, Dictionary<string, FileInfoDto> Files);
    internal sealed record SendFile(string Id, string Path);
    internal sealed record DiscoverySocket(UdpClient Client, string InterfaceName, IPAddress Address);
    internal sealed record RegisterInfo(string Alias, string Version, string? DeviceModel, string? DeviceType, string Fingerprint, int Port, string Protocol, bool Download);
    internal sealed record RegisterResponse(string Alias, string Version, string? DeviceModel, string? DeviceType, string Fingerprint, bool Download);
    internal sealed record FileInfoDto(string Id, string FileName, long Size, string FileType, string? Sha256, string? Preview = null);
    internal sealed record PrepareUploadRequest(RegisterInfo Info, Dictionary<string, FileInfoDto> Files);
    internal sealed record PrepareUploadResponse(string SessionId, Dictionary<string, string> Files);
    internal sealed record MulticastMessage(string Alias, string Version, string? DeviceModel, string? DeviceType, string Fingerprint, int Port, string Protocol, bool Download, bool Announce);
    internal sealed record LocalSendErrorMessage(string Message);
}
