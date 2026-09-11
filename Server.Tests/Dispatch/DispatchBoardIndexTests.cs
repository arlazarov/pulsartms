using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Microsoft.Extensions.Options;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
public class DispatchBoardIndexTests
{
  private static TruckDispatchBoardResponse Row(string number, int load) => new()
  {
    Key = number, TruckId = Guid.NewGuid(), TruckNumber = number, DriverName = "Aidar", TrailerNumber = "T44120",
    Dispatches = [new() {Id = Guid.NewGuid(), LoadNumber = load, OrderNumber = $"ORDER-{load}", CustomerName = "Customer",
      Stops = [new() {City = "Toronto", Name = "Warehouse"}]}]
  };

  [Fact]
  public void SourceAndResponseMutationCannotChangeIndex()
  {
    var source = Row("54777", 9876);
    var id = source.Dispatches[0].Id;
    var index = new DispatchBoardIndex([source]);
    source.DriverName = "changed";
    source.Dispatches[0].Stops[0].City = "changed";
    source.Dispatches.Clear();
    var first = index.SelectPage(1, 12, "Toronto", null);
    var row = Assert.Single(first.Items);
    Assert.Equal("Aidar", row.DriverName);
    Assert.Equal(id, Assert.Single(row.Dispatches).Id);
    row.DriverName = "request mutation"; row.Dispatches.Clear();
    var second = index.SelectPage(1, 12, "987", null);
    Assert.Equal("Aidar", Assert.Single(second.Items).DriverName);
    Assert.Equal(id, Assert.Single(second.Items.Single().Dispatches).Id);
  }

  [Fact]
  public void PriorityOrderingAndPagingArePreserved()
  {
    var index = new DispatchBoardIndex([Row("54777", 9876), Row("11005", 5555), Row("11006", 5556)]);
    Assert.Equal("54777", Assert.Single(index.SelectPage(1, 12, "5", null).Items).TruckNumber);
    var page = index.SelectPage(2, 1, null, null);
    Assert.Equal(3, page.TotalCount);
    Assert.Equal("11006", Assert.Single(page.Items).TruckNumber);
    Assert.Equal(3, index.SelectPage(99, 1, "Warehouse", null).TotalCount);
    Assert.Empty(index.SelectPage(99, 1, "Warehouse", null).Items);
  }

  [Fact]
  public async Task CacheSharesImmutableIndexAndRespectsInvalidation()
  {
    using var cache = new ReadCache(Options.Create(new SynchronizationOptions()));
    var calls = 0;
    async Task<DispatchBoardIndex> Load()
    {
      Interlocked.Increment(ref calls); await Task.Delay(10);
      return new([Row("54777", 9876)]);
    }
    var copies = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetAsync("board", "test", Load)));
    Assert.Equal(1, calls);
    Assert.All(copies, copy => Assert.Same(copies[0], copy));
    cache.Invalidate("board");
    Assert.NotSame(copies[0], await cache.GetAsync("board", "test", Load));
    Assert.Equal(2, calls);
  }

  [Fact]
  public void WarmPageAllocationDoesNotGrowWithFleetSize()
  {
    static long Measure(int count)
    {
      var index = new DispatchBoardIndex(Enumerable.Range(0, count).Select(i => Row((11000+i).ToString(), i)));
      _ = index.SelectPage(1, 12, null, null);
      var before = GC.GetAllocatedBytesForCurrentThread();
      for (var i = 0; i < 50; i++) Assert.Equal(12, index.SelectPage(1, 12, null, null).Items.Count);
      return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    var small = Measure(100);
    var large = Measure(1000);
    Assert.True(large < small * 1.2, $"100 trucks: {small}; 1000 trucks: {large}");
  }

  [Fact]
  public async Task OversizedIndexIsNotRetained()
  {
    using var cache = new ReadCache(Options.Create(new SynchronizationOptions()));
    var row = Row("54777", 9876); row.DriverName = new string('x', 4_194_304);
    var index = new DispatchBoardIndex([row]);
    Assert.True(index.EstimatedBytes > 8 * 1024 * 1024);
    var calls = 0;
    Task<DispatchBoardIndex> Load() { calls++; return Task.FromResult(index); }
    await cache.GetAsync("board", "large", Load); await cache.GetAsync("board", "large", Load);
    Assert.Equal(2, calls);
  }
}
