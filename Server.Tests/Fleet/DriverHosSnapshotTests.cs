using Application.Features.Fleet.Services;
using Domain.Models.Fleet;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class DriverHosSnapshotTests
{
  [Fact]
  public async Task ReadsReturnImmediatelyWhileRefreshIsPendingAndNeverExtendFreshness()
  {
    var time = new ManualTimeProvider();
    var snapshot = new DriverHosSnapshot(time);
    Assert.False(snapshot.TryBeginRefresh(false));
    Assert.Empty(await snapshot.GetClocksAsync(default));
    Assert.True(snapshot.TryBeginRefresh(false));
    Assert.False(snapshot.TryBeginRefresh(false));
    Assert.Empty(await snapshot.GetClocksAsync(default));
    var input = new DriverHosClocks
    {
      DriveMs = 100,
      UpdatedAt = time.GetUtcNow().UtcDateTime,
    };
    snapshot.Complete(
      new Dictionary<string, DriverHosClocks> { ["driver"] = input }
    );
    input.DriveMs = 999;
    var first = (await snapshot.GetClocksAsync(default))["driver"];
    Assert.Equal(100, first.DriveMs);
    first.DriveMs = 888;
    time.Advance(TimeSpan.FromSeconds(45));
    Assert.True(snapshot.TryBeginRefresh(false));
    Assert.Equal(
      100,
      (await snapshot.GetClocksAsync(default))["driver"].DriveMs
    );
    time.Advance(TimeSpan.FromSeconds(15));
    Assert.Empty(await snapshot.GetClocksAsync(default));
    snapshot.Complete(null);
    Assert.Empty(await snapshot.GetClocksAsync(default));
    Assert.False(snapshot.TryBeginRefresh(true));
    time.Advance(TimeSpan.FromSeconds(60));
    Assert.True(snapshot.TryBeginRefresh(true));
  }

  [Fact]
  public async Task FutureClocksAndOversizedSnapshotsAreNotReturned()
  {
    var time = new ManualTimeProvider();
    var snapshot = new DriverHosSnapshot(time);
    snapshot.Complete(
      new Dictionary<string, DriverHosClocks>
      {
        ["future"] = new()
        {
          UpdatedAt = time.GetUtcNow().UtcDateTime.AddSeconds(1),
        },
      }
    );
    Assert.Empty(await snapshot.GetClocksAsync(default));
    var rows = Enumerable
      .Range(0, 10001)
      .ToDictionary(
        x => x.ToString(),
        _ => new DriverHosClocks { UpdatedAt = time.GetUtcNow().UtcDateTime }
      );
    snapshot.Complete(rows);
    Assert.Empty(await snapshot.GetClocksAsync(default));
  }
}
