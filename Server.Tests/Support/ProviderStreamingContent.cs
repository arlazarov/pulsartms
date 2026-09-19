using System.Net;

namespace Server.Tests.Support;

internal sealed class ProviderStreamingContent(Stream stream) : HttpContent
{
  public int StreamOpens;

  protected override bool TryComputeLength(out long length)
  {
    length = 0;
    return false;
  }

  protected override Task SerializeToStreamAsync(
    Stream target,
    TransportContext? context
  ) =>
    throw new InvalidOperationException(
      "Response buffering is not allowed by this fixture."
    );

  protected override Task<Stream> CreateContentReadStreamAsync()
  {
    StreamOpens++;
    return Task.FromResult(stream);
  }
}

internal sealed class EndlessJsonStringStream : Stream
{
  public long ReadBytes;
  public bool Disposed;
  public override bool CanRead => true;
  public override bool CanWrite => false;
  public override bool CanSeek => false;
  public override long Length => throw new NotSupportedException();
  public override long Position
  {
    get => ReadBytes;
    set => throw new NotSupportedException();
  }

  public override int Read(byte[] buffer, int offset, int count)
  {
    Array.Fill(buffer, (byte)'x', offset, count);
    if (ReadBytes == 0 && count > 0)
      buffer[offset] = (byte)'"';
    ReadBytes += count;
    return count;
  }

  public override ValueTask<int> ReadAsync(
    Memory<byte> buffer,
    CancellationToken ct = default
  )
  {
    ct.ThrowIfCancellationRequested();
    buffer.Span.Fill((byte)'x');
    if (ReadBytes == 0 && buffer.Length > 0)
      buffer.Span[0] = (byte)'"';
    ReadBytes += buffer.Length;
    return ValueTask.FromResult(buffer.Length);
  }

  protected override void Dispose(bool disposing)
  {
    Disposed = true;
    base.Dispose(disposing);
  }

  public override void Flush() => throw new NotSupportedException();

  public override long Seek(long offset, SeekOrigin origin) =>
    throw new NotSupportedException();

  public override void SetLength(long value) =>
    throw new NotSupportedException();

  public override void Write(byte[] buffer, int offset, int count) =>
    throw new NotSupportedException();
}

internal sealed class DelayedProviderStream : Stream
{
  public override bool CanRead => true;
  public override bool CanWrite => false;
  public override bool CanSeek => false;
  public override long Length => throw new NotSupportedException();
  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override int Read(byte[] buffer, int offset, int count) =>
    throw new NotSupportedException();

  public override async ValueTask<int> ReadAsync(
    Memory<byte> buffer,
    CancellationToken ct = default
  )
  {
    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    return 0;
  }

  public override void Flush() => throw new NotSupportedException();

  public override long Seek(long offset, SeekOrigin origin) =>
    throw new NotSupportedException();

  public override void SetLength(long value) =>
    throw new NotSupportedException();

  public override void Write(byte[] buffer, int offset, int count) =>
    throw new NotSupportedException();
}
