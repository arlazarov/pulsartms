using Application.Features.Dispatch.Models;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Domain.Entities;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Policies;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Routing;

// Stage 4e of docs/architecture/current-work.md: a prepared summary's fuel
// hand-over line was drawn with the hours read when it was prepared. The
// hours are read fresh; a summary whose line they now draw otherwise is
// shown as prepared, marked stale by its duty and refreshing, and made
// due - never presented as current, and never stealing the ticket of a
// preparation under way.
[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class PlanningSummaryDutyTests
{
  private static readonly Guid Truck = Guid.NewGuid();
  private static readonly DispatchResponse Current = new()
  {
    Id = Guid.NewGuid(),
    LoadNumber = 1412,
    TruckId = Truck,
    Status = "in_transit",
    AssignmentRevision = 0,
  };

  [Fact]
  public void TheSameDutyIsCurrentAndAnotherIsStaleAndDue()
  {
    var f = new Fixture();
    f.Publish(f.Prepared(Driving()));

    var same = f.Reader.Read(f.Inputs(Driving()));
    var notDue = f.Cache.Take();
    var off = f.Reader.Read(f.Inputs(OffDuty()));
    var due = f.Cache.Take();

    Assert.Empty(same.StaleDependencies);
    Assert.Null(notDue);
    Assert.Equal(
      [AutomaticPlanningResult.DutyDependency],
      off.StaleDependencies
    );
    Assert.True(off.IsRefreshing);
    Assert.Equal(PlanningSummaryReader.DutyChanged, off.Message);
    Assert.Null(same.Message);
    Assert.Equal(
      same.State!.Plan!.FuelPlan!.IssueState,
      off.State!.Plan!.FuelPlan!.IssueState
    );
    Assert.NotNull(due);
  }

  // The duty changes while a preparation made with the old one is under
  // way: the reader does not take its ticket, so it publishes; the next
  // read finds it behind as well and asks again, and the preparation with
  // the new duty is current.
  [Fact]
  public void ADutyChangeDuringAPreparationIsCaughtByTheNextRead()
  {
    var f = new Fixture();
    f.Publish(f.Prepared(Driving()));
    f.Reader.Read(f.Inputs(OffDuty()));
    var underWay = f.Cache.Take()!;

    var during = f.Reader.Read(f.Inputs(OffDuty()));
    var old = f.Prepared(Driving());
    f.Cache.Complete(underWay, f.Signature, old);
    var behind = f.Reader.Read(f.Inputs(OffDuty()));
    var again = f.Cache.Take()!;
    f.Cache.Complete(again, f.Signature, f.Prepared(OffDuty()));
    var current = f.Reader.Read(f.Inputs(OffDuty()));

    Assert.Equal(
      [AutomaticPlanningResult.DutyDependency],
      during.StaleDependencies
    );
    Assert.Equal(old.State!.Plan!.Id, behind.State!.Plan!.Id);
    Assert.Equal(
      [AutomaticPlanningResult.DutyDependency],
      behind.StaleDependencies
    );
    Assert.Empty(current.StaleDependencies);
    Assert.Null(f.Cache.Take());
  }

  private static DriverHosClocks Driving() => Clocks("driving");

  private static DriverHosClocks OffDuty() => Clocks("offDuty");

  private static DriverHosClocks Clocks(string duty) =>
    new()
    {
      CurrentDutyStatus = duty,
      ShiftMs = (long)TimeSpan.FromHours(9).TotalMilliseconds,
      UpdatedAt = DateTime.UtcNow,
    };

  private sealed class Fixture
  {
    private readonly FuelIssueWindow window = new(
      Options.Create(new FuelIssueOptions()),
      TimeProvider.System
    );

    public Fixture()
    {
      Reader = new(
        Cache,
        null!,
        null!,
        new TestCompany(),
        TestCache.Create(),
        null!,
        window
      );
      Signature = Reader.Signature(Inputs(null));
      Cache.Keep(Key, Signature);
    }

    public PlanningSummaryCache Cache { get; } = new(TimeProvider.System);
    public PlanningSummaryReader Reader { get; }
    public string Signature { get; }
    public PlanningSummaryCache.Key Key => new(Company.Amf, Truck);

    public TruckPlanningInputs Inputs(DriverHosClocks? hos) =>
      new(FuelWorkFixture.Capture(Truck, [Current]).Itinerary, hos)
      {
        CurrentWork = new(Current.Id, null),
        CurrentAssignmentRevision = 0,
      };

    // A summary prepared with these hours: its fuel line drawn with them.
    public AutomaticPlanningResult Prepared(DriverHosClocks hos)
    {
      var fuel = new FuelPlan
      {
        Stops =
        [
          new()
          {
            StationId = Guid.NewGuid(),
            EstimatedArrival = DateTimeOffset.UtcNow.AddHours(1),
            ArrivalGallons = 60,
          },
        ],
      };
      window.Apply(fuel, hos);
      var plan = new RoutePlan
      {
        Id = Guid.NewGuid(),
        DispatchId = Current.Id,
        TruckId = Truck,
        Version = 1,
        FuelPlan = fuel,
      };
      return new(
        Truck,
        Current.Id,
        Current.LoadNumber,
        new(new(), plan, null, null, null, true),
        null
      )
      {
        CalculatedAt = DateTimeOffset.UtcNow,
      };
    }

    public void Publish(AutomaticPlanningResult result) =>
      Cache.Complete(Cache.Take()!, Signature, result);
  }
}
