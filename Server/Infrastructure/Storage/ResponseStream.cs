namespace Infrastructure.Storage;

// A provider download read as it arrives; disposing it releases the
// response, so no file is buffered whole.
public sealed class ResponseStream(HttpResponseMessage response, Stream inner)
  : Stream
{
  public override bool CanRead => true;
  public override bool CanSeek => false;
  public override bool CanWrite => false;
  public override long Length =>
    response.Content.Headers.ContentLength ?? throw new NotSupportedException();
  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override int Read(byte[] buffer, int offset, int count) =>
    inner.Read(buffer, offset, count);

  public override ValueTask<int> ReadAsync(
    Memory<byte> buffer,
    CancellationToken ct = default
  ) => inner.ReadAsync(buffer, ct);

  public override void Flush() { }

  public override long Seek(long offset, SeekOrigin origin) =>
    throw new NotSupportedException();

  public override void SetLength(long value) =>
    throw new NotSupportedException();

  public override void Write(byte[] buffer, int offset, int count) =>
    throw new NotSupportedException();

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      inner.Dispose();
      response.Dispose();
    }
    base.Dispose(disposing);
  }
}
