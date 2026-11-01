namespace SeanShell.Plugin.DotNet;

// Parsers use synchronous streams on the scan worker. Check cancellation between
// their reads as well as traversal steps; an OS read already in progress may finish.
internal sealed class CancellationReadStream(Stream stream, CancellationToken cancellationToken) : Stream
{
    public override bool CanRead => stream.CanRead;

    public override bool CanSeek => stream.CanSeek;

    public override bool CanWrite => false;

    public override long Length => stream.Length;

    public override long Position
    {
        get => stream.Position;
        set => stream.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = stream.Read(buffer, offset, count);
        cancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = stream.Read(buffer);
        cancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);

    public override void Flush() => stream.Flush();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            stream.Dispose();
        }

        base.Dispose(disposing);
    }
}
