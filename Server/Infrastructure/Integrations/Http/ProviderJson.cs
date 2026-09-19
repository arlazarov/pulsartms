using System.Text.Json;

namespace Infrastructure.Integrations.Http;

internal sealed class ProviderReadBudget
{
  public const int MaximumPageBytes = 8 * 1024 * 1024;
  private const long MaximumTotalBytes = 64 * 1024 * 1024;
  private long consumed;
  private int pages;

  public void BeginPage()
  {
    if (++pages > 256)
      throw TooLarge();
  }

  public void Consume(int count)
  {
    consumed += count;
    if (consumed > MaximumTotalBytes)
      throw TooLarge();
  }

  public static InvalidOperationException TooLarge() =>
    new("Provider response exceeded the bounded import limit.");
}

internal static class ProviderJson
{
  private static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );

  public static async Task<T?> ReadAsync<T>(
    HttpResponseMessage response,
    ProviderReadBudget budget,
    CancellationToken ct
  )
  {
    budget.BeginPage();
    if (
      response.Content.Headers.ContentLength
      > ProviderReadBudget.MaximumPageBytes
    )
      throw ProviderReadBudget.TooLarge();
    await using var stream = new BoundedStream(
      await response.Content.ReadAsStreamAsync(ct),
      budget
    );
    return await JsonSerializer.DeserializeAsync<T>(stream, Json, ct);
  }

  private sealed class BoundedStream(Stream inner, ProviderReadBudget budget)
    : Stream
  {
    private int consumed;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
      Count(
        inner.Read(
          buffer,
          offset,
          Math.Min(count, ProviderReadBudget.MaximumPageBytes - consumed + 1)
        )
      );

    public override async ValueTask<int> ReadAsync(
      Memory<byte> buffer,
      CancellationToken ct = default
    ) =>
      Count(
        await inner.ReadAsync(
          buffer[
            ..Math.Min(
              buffer.Length,
              ProviderReadBudget.MaximumPageBytes - consumed + 1
            )
          ],
          ct
        )
      );

    private int Count(int count)
    {
      consumed += count;
      if (consumed > ProviderReadBudget.MaximumPageBytes)
        throw ProviderReadBudget.TooLarge();
      budget.Consume(count);
      return count;
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        inner.Dispose();
      base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync() => inner.DisposeAsync();

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
      throw new NotSupportedException();

    public override void SetLength(long value) =>
      throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException();
  }
}
