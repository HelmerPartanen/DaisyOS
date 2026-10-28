using System.Buffers.Binary;
using DaisyOS.Core.Continuity;

namespace DaisyOS.Core.Tests;

public sealed class ContinuityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static ContinuityAccess Allowed => new(Enabled: true, SessionActive: true,
        Locked: false, Trusted: true, Authenticated: true, PermissionGranted: true);

    [Theory]
    [InlineData(ContinuityCapability.Clipboard)]
    [InlineData(ContinuityCapability.Files)]
    [InlineData(ContinuityCapability.Input)]
    [InlineData(ContinuityCapability.Handoff)]
    [InlineData(ContinuityCapability.Display)]
    public void DefaultsDenyEveryCapability(ContinuityCapability capability) =>
        Assert.NotEqual(ContinuityDenial.None, ContinuityPolicy.Evaluate(new(), capability, Now));

    [Fact]
    public void ClipboardRequiresEveryBoundary()
    {
        Assert.Equal(ContinuityDenial.None, Check(Allowed));
        Assert.Equal(ContinuityDenial.Disabled, Check(Allowed with { Enabled = false }));
        Assert.Equal(ContinuityDenial.Disabled, Check(Allowed with { Paused = true }));
        Assert.Equal(ContinuityDenial.SessionUnavailable, Check(Allowed with { SessionActive = false }));
        Assert.Equal(ContinuityDenial.SessionUnavailable, Check(Allowed with { Locked = true }));
        Assert.Equal(ContinuityDenial.Untrusted, Check(Allowed with { Trusted = false }));
        Assert.Equal(ContinuityDenial.Untrusted, Check(Allowed with { Revoked = true }));
        Assert.Equal(ContinuityDenial.Unauthenticated, Check(Allowed with { Authenticated = false }));
        Assert.Equal(ContinuityDenial.PermissionRequired, Check(Allowed with { PermissionGranted = false }));
    }

    [Fact]
    public void FileConsentMustBeExplicitAndUnexpired()
    {
        var consent = Allowed with { LocalConsentGranted = true, ConsentExpiresAt = Now.AddMinutes(1) };
        Assert.Equal(ContinuityDenial.None, CheckFile(consent));
        Assert.Equal(ContinuityDenial.LocalConsentRequired, CheckFile(Allowed));
        Assert.Equal(ContinuityDenial.LocalConsentRequired, CheckFile(consent with { LocalConsentGranted = false }));
        Assert.Equal(ContinuityDenial.LocalConsentRequired, CheckFile(consent with { ConsentExpiresAt = null }));
        Assert.Equal(ContinuityDenial.LocalConsentRequired, CheckFile(consent with { ConsentExpiresAt = Now }));
        Assert.Equal(ContinuityDenial.LocalConsentRequired, CheckFile(consent with { ConsentExpiresAt = Now.AddTicks(-1) }));
        Assert.Equal(ContinuityDenial.Untrusted, CheckFile(consent with { Revoked = true }));
    }

    [Theory]
    [InlineData(ContinuityCapability.Input)]
    [InlineData(ContinuityCapability.Handoff)]
    [InlineData(ContinuityCapability.Display)]
    [InlineData((ContinuityCapability)999)]
    public void LaterAndUnknownCapabilitiesFailClosed(ContinuityCapability capability) =>
        Assert.Equal(ContinuityDenial.Unsupported, ContinuityPolicy.Evaluate(Allowed, capability, Now));

    [Fact]
    public async Task FramesRoundTripAndEofIsDistinct()
    {
        using var stream = new MemoryStream();
        await ContinuityFraming.WriteAsync(stream, new byte[] { 1, 2, 3 });
        await ContinuityFraming.WriteAsync(stream, new byte[] { 4 });
        stream.Position = 0;
        Assert.Equal(new byte[] { 1, 2, 3 }, await ContinuityFraming.ReadAsync(stream));
        Assert.Equal(new byte[] { 4 }, await ContinuityFraming.ReadAsync(stream));
        Assert.Null(await ContinuityFraming.ReadAsync(stream));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData((uint)ContinuityFraming.MaxEnvelopeBytes + 1)]
    [InlineData(uint.MaxValue)]
    public async Task HostileLengthsFailBeforePayloadRead(uint length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, length);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await ContinuityFraming.ReadAsync(stream));
    }

    [Theory]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 0, 0, 0, 2, 1 })]
    public async Task TruncatedFramesAreErrors(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await ContinuityFraming.ReadAsync(stream));
    }

    [Fact]
    public async Task MaximumFrameWorksAndOversizedWritesLeaveStreamUntouched()
    {
        using var stream = new MemoryStream();
        var payload = new byte[ContinuityFraming.MaxEnvelopeBytes];
        payload[^1] = 42;
        await ContinuityFraming.WriteAsync(stream, payload);
        stream.Position = 0;
        Assert.Equal(payload, await ContinuityFraming.ReadAsync(stream));
        var length = stream.Length;
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await ContinuityFraming.WriteAsync(stream, new byte[ContinuityFraming.MaxEnvelopeBytes + 1]));
        Assert.Equal(length, stream.Length);
    }

    [Fact]
    public async Task CancelledReadPropagates()
    {
        using var stream = new MemoryStream(new byte[] { 0, 0, 0, 1, 42 });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await ContinuityFraming.ReadAsync(stream, cancellation.Token));
    }

    private static ContinuityDenial Check(ContinuityAccess access) =>
        ContinuityPolicy.Evaluate(access, ContinuityCapability.Clipboard, Now);
    private static ContinuityDenial CheckFile(ContinuityAccess access) =>
        ContinuityPolicy.Evaluate(access, ContinuityCapability.Files, Now);
}
