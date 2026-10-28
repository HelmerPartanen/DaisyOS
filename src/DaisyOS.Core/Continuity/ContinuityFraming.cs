using System.Buffers.Binary;

namespace DaisyOS.Core.Continuity;

/// <summary>
/// Length framing for one authenticated feature stream. This is not a transport
/// or authentication implementation. Callers own stream deadlines and disposal.
/// </summary>
public static class ContinuityFraming
{
    public const int MaxEnvelopeBytes = 1024 * 1024 + 4096;

    public static async ValueTask<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (first == 0) return null; // EOF is valid only between complete envelopes.
        await stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length is 0 or > MaxEnvelopeBytes)
            throw new InvalidDataException("Invalid Continuity envelope length.");
        var payload = new byte[(int)length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    public static async ValueTask WriteAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (payload.Length is 0 or > MaxEnvelopeBytes)
            throw new InvalidDataException("Invalid Continuity envelope length.");
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }
}
