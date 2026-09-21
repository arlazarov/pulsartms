using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class StopOperationTests
{
  [Theory]
  [InlineData("Driver start", "No truck", true)]
  [InlineData("Driver start", "Loaded", false)]
  [InlineData("Collect truck", "Bobtail", true)]
  [InlineData("Collect trailer", "Bobtail", false)]
  [InlineData("Collect trailer", "Empty", true)]
  [InlineData("Pick Up", "Loaded", true)]
  [InlineData("Pick Up", "Empty", false)]
  [InlineData("Drop Off", "Loaded", true)]
  [InlineData("Drop Off", "Empty", true)]
  [InlineData("Drop Off", "Unknown", true)]
  [InlineData("Waypoint", "Bobtail", true)]
  [InlineData(null, null, true)]
  [InlineData(null, "Empty", false)]
  public void ActionAndStateAreIndependentButMustBeCompatible(
    string? action,
    string? state,
    bool valid
  ) => Assert.Equal(valid, StopOperation.Valid(action, state));

  [Fact]
  public void PersonalTravelIsExcludedButBobtailEmptyAndLoadedLegsRemainInTruckRoute()
  {
    var load = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      Stops = new[]
      {
        ("Driver start", "No truck"),
        ("Collect truck", "Bobtail"),
        ("Collect trailer", "Empty"),
        ("Pick Up", "Loaded"),
        ("Drop Off", "Loaded"),
        ("Drop Off", "Empty"),
      }
        .Select(
          (value, index) =>
            new DispatchStop
            {
              Id = Guid.NewGuid(),
              Sequence = index + 1,
              Job = "Pick Up",
              ManualAction = value.Item1,
              ManualStateAfter = value.Item2,
            }
        )
        .ToList(),
    };
    var route = load.TruckItinerary();
    Assert.Equal(
      load.Stops.Skip(1).Select(s => s.Id),
      route.Stops.Select(s => s.Id)
    );
    Assert.Equal(
      new[] { "Bobtail", "Empty", "Loaded", "Loaded", "Empty" },
      route.Stops.Select(s => s.StateAfter)
    );
    Assert.All(
      load.Stops,
      s =>
      {
        Assert.Equal("Pick Up", s.Job);
        Assert.False(s.IsCompleted);
      }
    );
    Assert.Equal(route.Stops, route.TruckItinerary().Stops);
    var hash = BaseRouteService.Signature(load, new TruckRouteProfile());
    load.Stops[0].Address = "Changed personal departure";
    Assert.Equal(
      hash,
      BaseRouteService.Signature(load, new TruckRouteProfile())
    );
    load.Stops[1].ManualStateAfter = "Empty";
    Assert.Equal(
      hash,
      BaseRouteService.Signature(load, new TruckRouteProfile())
    );
  }

  [Fact]
  public void MissingCargoDoesNotImplyEmptyAndDeliveryDoesNotAssumeAllCargoRemoved()
  {
    var stops = new[]
    {
      new DispatchStop { Sequence = 1, Job = "Pick Up" },
      new DispatchStop { Sequence = 2, Job = "Drop Off" },
    };
    Assert.Equal(
      new[] { "Loaded", "Unknown" },
      StopOperation.Resolve(stops, null).Select(s => s.StateAfter)
    );
    Assert.All(
      stops,
      s =>
      {
        Assert.Null(s.Weight);
        Assert.Equal("", s.Commodity);
      }
    );
  }

  [Fact]
  public void UnresolvedPersonalGapCannotBecomeTruckDriving()
  {
    var load = new DispatchEntity
    {
      Stops =
      [
        new()
        {
          Sequence = 1,
          ManualAction = "Collect truck",
          ManualStateAfter = "Bobtail",
        },
        new()
        {
          Sequence = 2,
          ManualAction = "Driver start",
          ManualStateAfter = "No truck",
        },
        new()
        {
          Sequence = 3,
          ManualAction = "Collect truck",
          ManualStateAfter = "Bobtail",
        },
      ],
    };
    Assert.Empty(load.TruckItinerary().Stops);
  }

  [Fact]
  public void ImportedTruckAssignmentEndsAnExplicitPersonalPrefix()
  {
    var truck = Guid.NewGuid();
    var load = new DispatchEntity
    {
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          ManualAction = "Driver start",
          ManualStateAfter = "No truck",
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Pick Up",
          TruckId = truck,
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 3,
          Job = "Drop Off",
        },
      ],
    };
    Assert.Equal(
      load.Stops.Skip(1).Select(s => s.Id),
      load.TruckItinerary().Stops.Select(s => s.Id)
    );
    Assert.Equal(truck, load.TruckItinerary().TruckId);
  }
}
