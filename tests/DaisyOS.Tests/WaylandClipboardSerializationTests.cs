using System.Reflection;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Xunit;

namespace DaisyOS.Tests;

public sealed class WaylandClipboardSerializationTests
{
    [Fact]
    public async Task BitmapSerializationDoesNotPerformBlockingWritesToDestination()
    {
        var expected = new byte[128 * 1024];
        new Random(42).NextBytes(expected);
        using var bitmap = new ClipboardBitmap(expected);
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DataFormat.Bitmap, bitmap));
        using var output = new AsyncOnlyOutput();
        var serializer = typeof(WaylandPlatformOptions).Assembly
            .GetType("Avalonia.Wayland.Clipboard.WaylandClipboardImpl")!
            .GetMethod("SerializeForMimeAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

        await (Task)serializer.Invoke(null, [transfer, "image/png", output])!;

        Assert.Equal(expected, output.Bytes);
    }

    private sealed class ClipboardBitmap(byte[] bytes) : Bitmap(new EncodedBitmap(bytes)) { }

    private sealed class EncodedBitmap(byte[] bytes) : IBitmapImpl
    {
        public Vector Dpi => new(96, 96);
        public PixelSize PixelSize => new(200, 200);
        public int Version => 1;
        public void Save(Stream stream, BitmapEncoderOptions options) => stream.Write(bytes);
        public void Dispose() { }
    }

    private sealed class AsyncOnlyOutput : Stream
    {
        private readonly MemoryStream _buffer = new();
        public byte[] Bytes => _buffer.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _buffer.Length;
        public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Blocking pipe write");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Blocking pipe write");
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        {
            var task = WriteAsync(buffer, offset, count, CancellationToken.None);
            if (callback is not null) _ = task.ContinueWith(_ => callback(task), TaskScheduler.Default);
            return task;
        }
        public override void EndWrite(IAsyncResult asyncResult) => ((Task)asyncResult).GetAwaiter().GetResult();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _buffer.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _buffer.Dispose();
            base.Dispose(disposing);
        }
    }
}
