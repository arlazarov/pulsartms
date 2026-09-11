namespace Client.Services;

internal sealed class ResponsiveWriteStream(TimeProvider? clock = null) : MemoryStream
{
  public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
  {
    await Task.Delay(TimeSpan.FromMilliseconds(1), clock ?? TimeProvider.System, cancellationToken);
    await base.WriteAsync(buffer, cancellationToken);
  }
  public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
    WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
}
