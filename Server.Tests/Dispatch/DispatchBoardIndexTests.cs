using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
public class DispatchBoardIndexTests
{
  private static TruckDispatchBoardResponse Row(string number, int load) =>
    new()
    {
      Key = number,
      TruckId = Guid.NewGuid(),
      TruckNumber = number,
      DriverName = "Aidar",
      TrailerNumber = "T44120",
      Dispatches =
      [
        new()
        {
          Id = Guid.NewGuid(),
          LoadNumber = load,
          OrderNumber = $"ORDER-{load}",
          CustomerName = "Customer",
          Stops = [new() { City = "Toronto", Name = "Warehouse" }],
        },
      ],
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
    var first = index.SelectPage(1, 12, "Toronto", null, DriverScope.All);
    var row = Assert.Single(first.Items);
    Assert.Equal("Aidar", row.DriverName);
    Assert.Equal(id, Assert.Single(row.Dispatches).Id);
    row.DriverName = "request mutation";
    row.Dispatches.Clear();
    var second = index.SelectPage(1, 12, "987", null, DriverScope.All);
    Assert.Equal("Aidar", Assert.Single(second.Items).DriverName);
    Assert.Equal(id, Assert.Single(second.Items.Single().Dispatches).Id);
  }

  // Cards and Papers search active loads only (the owner, September 28):
  // a completed load neither matches nor comes back, a truck left with none
  // is not listed, and the Table's search and the unsearched board keep it.
  [Fact]
  public void AnActiveSearchNeitherMatchesNorReturnsACompletedLoad()
  {
    var mixed = Row("54777", 1413);
    var active = mixed.Dispatches[0].Id;
    var done = Guid.NewGuid();
    mixed.Dispatches.Add(
      new()
      {
        Id = done,
        LoadNumber = 1385,
        OrderNumber = "ORDER-1385",
        CustomerName = "Delivered Customer",
        Status = "completed",
        Stops = [new() { City = "Amsterdam", Name = "Dock" }],
      }
    );
    var finished = Row("11008", 1395);
    finished.Dispatches[0].Status = "completed";
    var index = new DispatchBoardIndex([mixed, finished]);
    string[] Loads(string query, bool activeSearch) =>
      [
        .. index
          .SelectPage(
            1,
            12,
            query,
            null,
            DriverScope.All,
            activeSearch: activeSearch
          )
          .Items.SelectMany(x => x.Dispatches)
          .Select(x => x.Id.ToString()),
      ];

    Assert.Empty(Loads("Amsterdam", true));
    Assert.Empty(Loads("1395", true));
    Assert.Empty(Loads("11008", true));
    Assert.Equal([active.ToString()], Loads("54777", true));
    Assert.Equal([active.ToString()], Loads("Toronto", true));
    Assert.Equal(
      0,
      index
        .SelectPage(1, 12, "Amsterdam", null, DriverScope.All, null, true)
        .TotalCount
    );

    Assert.Equal([done.ToString()], Loads("Amsterdam", false).Skip(1));
    Assert.Contains(finished.Dispatches[0].Id.ToString(), Loads("1395", false));
    Assert.Equal(3, Loads("", true).Length);
  }

  // A chosen driver group narrows the shared index, not a copy per user: a
  // truck its drivers are on, or a load one of them drives. All is all.
  [Fact]
  public void AChosenDriverGroupNarrowsThePageNotTheIndex()
  {
    var theirs = Row("11005", 5555);
    var bound = Row("11006", 5556);
    var driving = Row("11007", 5557);
    var driver = Guid.NewGuid();
    driving.Dispatches[0].DriverId = driver;
    var index = new DispatchBoardIndex([theirs, bound, driving]);
    var group = new DriverScope(
      Guid.NewGuid(),
      "West",
      [driver],
      [bound.TruckId!.Value]
    );

    var page = index.SelectPage(1, 12, null, null, group);

    Assert.Equal(["11006", "11007"], page.Items.Select(x => x.TruckNumber));
    Assert.Equal(2, page.TotalCount);
    Assert.Equal(
      3,
      index.SelectPage(1, 12, null, null, DriverScope.All).TotalCount
    );
  }

  [Fact]
  public void PriorityOrderingAndPagingArePreserved()
  {
    var index = new DispatchBoardIndex(
      [Row("54777", 9876), Row("11005", 5555), Row("11006", 5556)]
    );
    Assert.Equal(
      "54777",
      Assert
        .Single(index.SelectPage(1, 12, "5", null, DriverScope.All).Items)
        .TruckNumber
    );
    var page = index.SelectPage(2, 1, null, null, DriverScope.All);
    Assert.Equal(3, page.TotalCount);
    Assert.Equal("11006", Assert.Single(page.Items).TruckNumber);
    Assert.Equal(
      3,
      index.SelectPage(99, 1, "Warehouse", null, DriverScope.All).TotalCount
    );
    Assert.Empty(
      index.SelectPage(99, 1, "Warehouse", null, DriverScope.All).Items
    );
  }

  [Fact]
  public async Task CacheSharesImmutableIndexAndRespectsInvalidation()
  {
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var calls = 0;
    async Task<DispatchBoardIndex> Load()
    {
      Interlocked.Increment(ref calls);
      await Task.Delay(10);
      return new([Row("54777", 9876)]);
    }
    var copies = await Task.WhenAll(
      Enumerable.Range(0, 20).Select(_ => cache.GetAsync("board", "test", Load))
    );
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
      var index = new DispatchBoardIndex(
        Enumerable.Range(0, count).Select(i => Row((11000 + i).ToString(), i))
      );
      _ = index.SelectPage(1, 12, null, null, DriverScope.All);
      var before = GC.GetAllocatedBytesForCurrentThread();
      for (var i = 0; i < 50; i++)
        Assert.Equal(
          12,
          index.SelectPage(1, 12, null, null, DriverScope.All).Items.Count
        );
      return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    var small = Measure(100);
    var large = Measure(1000);
    Assert.True(
      large < small * 1.2,
      $"100 trucks: {small}; 1000 trucks: {large}"
    );
  }

  [Fact]
  public async Task OversizedIndexIsNotRetained()
  {
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var row = Row("54777", 9876);
    row.DriverName = new string('x', 4_194_304);
    var index = new DispatchBoardIndex([row]);
    Assert.True(index.EstimatedBytes > 8 * 1024 * 1024);
    var calls = 0;
    Task<DispatchBoardIndex> Load()
    {
      calls++;
      return Task.FromResult(index);
    }
    await cache.GetAsync("board", "large", Load);
    await cache.GetAsync("board", "large", Load);
    Assert.Equal(2, calls);
  }
}
