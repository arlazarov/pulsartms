using System.Security.Cryptography;
using Application.Storage;

namespace Server.Tests.Storage;

// What a provider can read from an upload: exactly the declared bytes, and
// nothing past a mismatch. Every check fails before the read that would
// hand over the last declared byte returns, so a provider that stops at the
// declared length never receives a complete wrong upload.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class StorageReadingTests
{
  private static readonly byte[] Content = [1, 2, 3, 4, 5, 6];

  [Fact]
  public async Task AnEmptyReadReturnsNothingAndConsumesNothingAnywhere()
  {
    var input = new CountingStream(Content);
    var reading = Reading(input, Content);
    var chunk = new byte[4];

    Assert.Equal(0, await reading.ReadAsync(Memory<byte>.Empty));
    Assert.Equal(0, input.Consumed);
    Assert.Equal(4, await reading.ReadAsync(chunk));
    Assert.Equal(0, await reading.ReadAsync(Memory<byte>.Empty));
    Assert.Equal(4, input.Consumed);
    Assert.Equal(2, await reading.ReadAsync(chunk));
    var afterEnd = input.Consumed;
    Assert.Equal(0, await reading.ReadAsync(Memory<byte>.Empty));
    Assert.Equal(0, await reading.ReadAsync(chunk));
    Assert.Equal(afterEnd, input.Consumed);
  }

  [Fact]
  public async Task AProviderStoppingAtTheDeclaredLengthGetsExactlyTheContent() =>
    Assert.Equal(
      Content,
      await ProviderReadAsync(Reading(new CountingStream(Content), Content))
    );

  // The input is longer than declared. The provider asks only for the
  // declared length, as one sending Content-Length does; the read that
  // would complete it fails, so the last bytes never reach the provider.
  [Fact]
  public async Task ExcessInputFailsBeforeTheLastDeclaredBytesAreHandedOver()
  {
    var received = new List<byte>();
    var reading = Reading(new CountingStream([.. Content, 7]), Content);

    await Assert.ThrowsAsync<StorageContentMismatchException>(
      () => ProviderReadAsync(reading, received, chunk: 4)
    );

    Assert.Equal([1, 2, 3, 4], received);
  }

  [Fact]
  public async Task ShortOrDifferentInputFailsBeforeTheUploadCouldComplete()
  {
    var shortReceived = new List<byte>();
    await Assert.ThrowsAsync<StorageContentMismatchException>(
      () =>
        ProviderReadAsync(
          Reading(new CountingStream([1, 2, 3]), Content),
          shortReceived,
          chunk: 4
        )
    );
    Assert.True(shortReceived.Count < Content.Length);

    var differentReceived = new List<byte>();
    await Assert.ThrowsAsync<StorageContentMismatchException>(
      () =>
        ProviderReadAsync(
          Reading(new CountingStream([1, 2, 3, 4, 5, 9]), Content),
          differentReceived,
          chunk: 4
        )
    );
    Assert.Equal([1, 2, 3, 4], differentReceived);
  }

  private static StorageReading Reading(Stream input, byte[] declared) =>
    new(
      input,
      declared.Length,
      Convert.ToHexStringLower(SHA256.HashData(declared))
    );

  // A provider that reads only up to the declared length, never past it.
  private static async Task<byte[]> ProviderReadAsync(
    StorageReading reading,
    List<byte>? received = null,
    int chunk = 4
  )
  {
    received ??= [];
    var buffer = new byte[chunk];
    while (received.Count < reading.Length)
    {
      var want = (int)Math.Min(chunk, reading.Length - received.Count);
      var count = await reading.ReadAsync(buffer.AsMemory(0, want));
      if (count == 0)
        break;
      received.AddRange(buffer[..count]);
    }
    return [.. received];
  }

  private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
  {
    public long Consumed => Position;
  }
}
