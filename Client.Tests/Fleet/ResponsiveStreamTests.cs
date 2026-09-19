using System.Text.Json;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public class ResponsiveStreamTests
{
  [Fact]
  public async Task LargeJsonRoundTripsThroughYieldingStreams()
  {
    var expected = Enumerable
      .Range(0, 20000)
      .Select(i => $"Stop {i} · Montréal 🚚")
      .ToArray();
    var writeClock = new ScheduledDelayClock();
    using var output = new ResponsiveWriteStream(writeClock);
    var writing = JsonSerializer.SerializeAsync(output, expected);
    Assert.False(writing.IsCompleted);
    Assert.Equal(0, output.Length);
    await writeClock.CompleteAsync(writing);
    Assert.True(writeClock.TimerCount >= 1);
    output.Position = 0;
    var readClock = new ScheduledDelayClock();
    using var input = new ResponsiveReadStream(output, readClock);
    var reading = JsonSerializer.DeserializeAsync<string[]>(input).AsTask();
    Assert.False(reading.IsCompleted);
    Assert.Equal(0, output.Position);
    await readClock.CompleteAsync(reading);
    Assert.True(readClock.TimerCount > 1);
    Assert.Equal(expected, await reading);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task BothOverloadsYieldBeforeReadingOrWritingAlreadyBufferedData(
    bool arrayOverload
  )
  {
    var clock = new FakeTimeProvider();
    var bytes = new byte[] { 1, 2, 3 };
    using var output = new ResponsiveWriteStream(clock);
    var writing = arrayOverload
      ? output.WriteAsync(bytes, 0, bytes.Length)
      : output.WriteAsync(bytes.AsMemory()).AsTask();
    Assert.False(writing.IsCompleted);
    Assert.Equal(0, output.Length);
    clock.Advance(TimeSpan.FromMilliseconds(1));
    await writing;
    output.Position = 0;
    using var input = new ResponsiveReadStream(output, clock);
    var buffer = new byte[bytes.Length];
    var reading = arrayOverload
      ? input.ReadAsync(buffer, 0, buffer.Length)
      : input.ReadAsync(buffer.AsMemory()).AsTask();
    Assert.False(reading.IsCompleted);
    Assert.Equal(0, output.Position);
    Assert.Equal(new byte[bytes.Length], buffer);
    clock.Advance(TimeSpan.FromMilliseconds(1));
    Assert.Equal(bytes.Length, await reading);
    Assert.Equal(bytes, buffer);
  }

  [Fact]
  public async Task ReadsAreBoundedAndBothOverloadsWork()
  {
    using var source = new MemoryStream(new byte[40000]);
    using var stream = new ResponsiveReadStream(source);
    var buffer = new byte[40000];
    Assert.Equal(16384, await stream.ReadAsync(buffer.AsMemory()));
    Assert.Equal(
      16384,
      await stream.ReadAsync(buffer, 0, buffer.Length, default)
    );
    Assert.Equal(7232, await stream.ReadAsync(buffer.AsMemory()));
    Assert.Equal(0, await stream.ReadAsync(buffer.AsMemory()));
  }

  [Fact]
  public async Task CancellationWhileSuspendedDoesNotConsumeOrWriteData()
  {
    var clock = new FakeTimeProvider();
    using var cancellation = new CancellationTokenSource();
    using var source = new MemoryStream(new byte[100]);
    using var input = new ResponsiveReadStream(source, clock);
    using var output = new ResponsiveWriteStream(clock);
    var reading = input
      .ReadAsync(new byte[100].AsMemory(), cancellation.Token)
      .AsTask();
    var writing = output
      .WriteAsync(new byte[100].AsMemory(), cancellation.Token)
      .AsTask();
    Assert.False(reading.IsCompleted);
    Assert.False(writing.IsCompleted);

    cancellation.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing);
    Assert.Equal(0, source.Position);
    Assert.Equal(0, output.Length);
  }

  [Fact]
  public async Task CancellationDoesNotConsumeOrWriteData()
  {
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    using var source = new MemoryStream(new byte[100]);
    using var input = new ResponsiveReadStream(source);
    using var output = new ResponsiveWriteStream();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      async () =>
        _ = await input.ReadAsync(new byte[100].AsMemory(), cancellation.Token)
    );
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      async () =>
        await output.WriteAsync(new byte[100].AsMemory(), cancellation.Token)
    );
    Assert.Equal(0, source.Position);
    Assert.Equal(0, output.Length);
  }
}
