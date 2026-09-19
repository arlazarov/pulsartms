namespace Client.Services;

// Bound each JSON input chunk and yield even when HTTP data is already
// buffered.
internal sealed class ResponsiveReadStream(
  Stream source,
  TimeProvider? clock = null
) : Stream
{
  public override bool CanRead => true;
  public override bool CanSeek => false;
  public override bool CanWrite => false;
  public override long Length => throw new NotSupportedException();
  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override async ValueTask<int> ReadAsync(
    Memory<byte> buffer,
    CancellationToken cancellationToken = default
  )
  {
    await Task.Delay(
      TimeSpan.FromMilliseconds(1),
      clock ?? TimeProvider.System,
      cancellationToken
    );
    return await source.ReadAsync(
      buffer[..Math.Min(buffer.Length, 16384)],
      cancellationToken
    );
  }

  public override Task<int> ReadAsync(
    byte[] buffer,
    int offset,
    int count,
    CancellationToken cancellationToken
  ) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

  public override int Read(byte[] buffer, int offset, int count) =>
    throw new NotSupportedException();

  public override void Flush() => throw new NotSupportedException();

  public override long Seek(long offset, SeekOrigin origin) =>
    throw new NotSupportedException();

  public override void SetLength(long value) =>
    throw new NotSupportedException();

  public override void Write(byte[] buffer, int offset, int count) =>
    throw new NotSupportedException();
}
