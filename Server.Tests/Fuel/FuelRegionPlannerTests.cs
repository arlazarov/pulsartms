using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure.Integrations.GeoTimeZone;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public class FuelRegionPlannerTests
{
  [Theory]
  [InlineData("native-prefix")]
  [InlineData("missing-root")]
  [InlineData("wrong-truck")]
  [InlineData("wrong-revision")]
  public async Task ArrivalPolicyRequiresTheExactTerminalAssignment(
    string scenario
  )
  {
    var sender = new Sender();
    var plan = new RoutePlan
    {
      DispatchId = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      Route = new()
      {
        Miles = 100,
        Legs = [new(100, 6000, [new(40, -101), new(40, -100)])],
      },
    };
    var router = new Router();
    await using var fixture = await Fixture.CreateAsync(router, sender, plan);
    var terminal = sender.Rows.Single().Dispatches.Single();
    foreach (var stop in terminal.Stops)
      stop.Id = Guid.NewGuid();
    var loads = new List<DispatchResponse>
    {
      new()
      {
        Id = Guid.NewGuid(),
        ExecutionLegId = Guid.NewGuid(),
        AssignmentRevision = 7,
        ExecutionStatus = "active",
        TruckId = plan.TruckId,
      },
      terminal,
    };
    if (scenario == "missing-root")
      loads.Remove(terminal);
    if (scenario == "wrong-truck")
      terminal.TruckId = Guid.NewGuid();
    if (scenario == "wrong-revision")
      plan.AssignmentRevision++;
    var local = new PricedFuelStation(
      new() { StationId = Guid.NewGuid(), Point = new(40, -100) },
      3,
      3
    );

    if (scenario == "native-prefix")
    {
      var policy = (
        await fixture.Planner.BuildAsync(
          plan,
          BorderProfile(),
          [local],
          0,
          default,
          suppliedInputs: FuelWorkFixture.Capture(plan.TruckId, loads)
        )
      ).Policy;
      Assert.Equal(local.Station.StationId, policy.EscapeStationId);
    }
    else
    {
      var error = await Assert.ThrowsAsync<RoutePlanningException>(
        () =>
          fixture.Planner.BuildAsync(
            plan,
            BorderProfile(),
            [local],
            0,
            default,
            suppliedInputs: FuelWorkFixture.Capture(plan.TruckId, loads)
          )
      );
      Assert.Contains("selected assignment changed", error.Message);
    }
    Assert.Equal(0, router.Calls);
  }

  [Theory]
  [InlineData(true, false)]
  [InlineData(false, false)]
  [InlineData(false, true)]
  public async Task DeliveryCountryKeepsFuelAccessOnItsSideOfTheBorder(
    bool canadianDelivery,
    bool crossedDuringLoad
  )
  {
    var canada = new RoutePoint(43.6532, -79.3832);
    var usa = new RoutePoint(42.8864, -78.8784);
    var delivery = canadianDelivery ? canada : usa;
    var origin =
      crossedDuringLoad ? canada
      : canadianDelivery ? new RoutePoint(43.8, -80)
      : new RoutePoint(42.7, -78.8);
    var plan = new RoutePlan
    {
      DispatchId = Guid.NewGuid(),
      Route = new()
      {
        Miles = 100,
        Legs = [new(100, 6000, [origin, delivery])],
      },
    };
    var local = new PricedFuelStation(
      new()
      {
        StationId = Guid.NewGuid(),
        Name = "Local fuel",
        Country = canadianDelivery ? "CA" : "US",
        Point = delivery,
      },
      6,
      6
    );
    var foreign = new PricedFuelStation(
      new()
      {
        StationId = Guid.NewGuid(),
        Name = "Cheaper across the border",
        Country = canadianDelivery ? "US" : "CA",
        Point = canadianDelivery ? usa : canada,
      },
      3,
      3
    );
    var router = new Router();
    await using var fixture = await Fixture.CreateAsync(
      router,
      new Sender(),
      plan
    );
    var policy = (
      await fixture.Planner.BuildAsync(
        plan,
        BorderProfile(),
        [local, foreign],
        0,
        default
      )
    ).Policy;
    Assert.Equal(local.Station.StationId, policy.EscapeStationId);
    Assert.Equal(0, router.Calls);
  }

  [Fact]
  public async Task SavedCrossBorderPickupAllowsFuelOnItsForeignRoadSection()
  {
    var id = Guid.NewGuid();
    var nextId = Guid.NewGuid();
    var toronto = new RoutePoint(43.6532, -79.3832);
    var buffalo = new RoutePoint(42.8864, -78.8784);
    var sender = new Sender
    {
      Rows =
      [
        new()
        {
          Dispatches =
          [
            new()
            {
              Id = id,
              Stops =
              [
                new()
                {
                  Sequence = 1,
                  Latitude = 43.8m,
                  Longitude = -80,
                },
                new()
                {
                  Sequence = 2,
                  Latitude = (decimal)toronto.Latitude,
                  Longitude = (decimal)toronto.Longitude,
                },
              ],
            },
            new()
            {
              Id = nextId,
              Stops =
              [
                new()
                {
                  Sequence = 1,
                  Latitude = (decimal)buffalo.Latitude,
                  Longitude = (decimal)buffalo.Longitude,
                },
              ],
            },
          ],
        },
      ],
    };
    var plan = new RoutePlan
    {
      DispatchId = id,
      Route = new()
      {
        Miles = 100,
        Legs = [new(100, 6000, [new(43.8, -80), toronto])],
      },
    };
    var station = new PricedFuelStation(
      new()
      {
        StationId = Guid.NewGuid(),
        Country = "US",
        Point = buffalo,
      },
      3,
      3
    );
    var router = new Router();
    var profile = BorderProfile();
    await using var fixture = await Fixture.CreateAsync(router, sender, plan);
    await fixture.SaveConnectionAsync(plan, profile, nextId);
    var policy = (
      await fixture.Planner.BuildAsync(plan, profile, [station], 0, default)
    ).Policy;
    Assert.Equal(station.Station.StationId, policy.EscapeStationId);
    Assert.Equal(nextId, policy.NextDispatchId);
    Assert.Equal(0, router.Calls);
  }

  private static TruckRouteProfile BorderProfile() =>
    new()
    {
      Confirmed = true,
      TankGallons = 250,
      Mpg = 5,
      ReserveGallons = 25,
      FillPercent = 100,
    };

  [Theory]
  [InlineData(-99, 50)]
  [InlineData(-97, 58)]
  public async Task ReserveUsesExplicitAccessEstimatesWithoutRouting(
    double longitude,
    double minimum
  )
  {
    var router = new Router();
    var profile = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 100,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
    };
    var plan = new RoutePlan
    {
      DispatchId = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      Route = new()
      {
        Miles = 100,
        Legs = [new(100, 6000, [new(40, -101), new(40, -100)])],
      },
    };
    var local = new PricedFuelStation(
      new()
      {
        StationId = Guid.NewGuid(),
        Name = "Expensive nearby",
        Point = new(40, -99.9),
      },
      6,
      6
    );
    var cheap = new PricedFuelStation(
      new()
      {
        StationId = Guid.NewGuid(),
        Name = "Affordable exit",
        Point = new(40, longitude),
      },
      3,
      3
    );
    await using var fixture = await Fixture.CreateAsync(
      router,
      new Sender(),
      plan
    );
    var planner = fixture.Planner;
    var policy = (
      await planner.BuildAsync(plan, profile, [local, cheap], 0, default)
    ).Policy;
    Assert.True(policy.PoorArea);
    Assert.Equal(cheap.Station.StationId, policy.EscapeStationId);
    Assert.Equal(
      FuelAccessEstimate.DistanceMiles(
        RouteGeometry.Distance(new(40, -100), cheap.Station.Point)
      ),
      policy.EscapeMiles
    );
    Assert.Equal(minimum, policy.MinimumGallons);
    Assert.Equal(
      profile.TankGallons * profile.FillPercent / 100,
      policy.TargetGallons
    );
    Assert.True(policy.EconomicPurchasesOnly);
    Assert.Equal(0, router.Calls);
    Assert.Null(router.Address);
    Assert.Contains("estimated", policy.Reason);
    Assert.Contains("not been road-checked", policy.Reason);
    Assert.NotEmpty(policy.Regions);
  }

  [Theory]
  [InlineData(false, 25, 125)]
  [InlineData(true, 25, 125)]
  [InlineData(false, 100, 140)]
  [InlineData(true, 100, 140)]
  public async Task MissingDestinationPricesPreserveReserveAndArrivalEstimates(
    bool foreignQuote,
    double reserve,
    double minimum
  )
  {
    var router = new Router();
    var plan = new RoutePlan
    {
      Route = new()
      {
        Miles = 100,
        Legs = [new(100, 6000, [new(43.8, -80), new(43.6532, -79.3832)])],
      },
    };
    await using var fixture = await Fixture.CreateAsync(
      router,
      new Sender(),
      plan
    );
    var planner = fixture.Planner;
    var profile = BorderProfile();
    profile.ReserveGallons = reserve;
    var prices = foreignQuote
      ? new List<PricedFuelStation>
      {
        new(
          new()
          {
            StationId = Guid.NewGuid(),
            Name = "Across the border",
            Country = "US",
            Point = new(42.8864, -78.8784),
          },
          3,
          3
        ),
      }
      : [];
    var policy = (
      await planner.BuildAsync(plan, profile, prices, 0, default)
    ).Policy;
    Assert.True(policy.PoorArea);
    Assert.True(policy.EconomicPurchasesOnly);
    Assert.Equal(minimum, policy.MinimumGallons);
    Assert.Equal(policy.MinimumGallons, policy.TargetGallons);
    Assert.Equal(0, policy.ReplacementPriceUsd);
    Assert.Equal(Guid.Empty, policy.EscapeStationId);
    Assert.Empty(policy.EscapeStationName);
    Assert.Equal(0, policy.EscapeMiles);
    Assert.Contains("coverage is unavailable", policy.Reason);
    var fuel = FuelOptimizer.Optimize(
      100,
      200,
      profile,
      [],
      1,
      false,
      arrivalPolicy: policy
    );
    Assert.Empty(fuel.Stops);
    Assert.Equal(180, fuel.ArrivalGallons);
    Assert.Equal(0, fuel.ExpectedFutureFuelCostUsd);
    var arrivals = FuelStopArrivals.Calculate(
      fuel,
      [
        new(
          plan.DispatchId,
          new(
            Guid.NewGuid(),
            "Delivery",
            "",
            1,
            plan.Route.Legs[^1].Points[^1]
          ),
          100
        ),
      ],
      profile
    );
    Assert.Equal(180, Assert.Single(arrivals).Gallons);
    Assert.Equal(0, router.Calls);
  }

  [Theory]
  [InlineData(5.696, true)]
  [InlineData(5.4, false)]
  [InlineData(5.604, false)]
  public async Task GoodDestinationValuesExtraFuelAtReplacementPriceWithoutForcingFullPurchases(
    double laterPrice,
    bool full
  )
  {
    var router = new Router();
    var profile = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 250,
      Mpg = 5,
      ReserveGallons = 25,
      FillPercent = 100,
    };
    var plan = new RoutePlan
    {
      DispatchId = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      Route = new()
      {
        Miles = 500,
        Legs = [new(500, 30000, [new(40, -101), new(40, -100)])],
      },
    };
    PricedFuelStation Price(double longitude, double price) =>
      new(
        new()
        {
          StationId = Guid.NewGuid(),
          Point = new(40, longitude),
          YourPrice = price,
          EconomicPrice = price,
        },
        price,
        price
      );
    var purchase = Price(-100.5, 5.604);
    var prices = new[]
    {
      purchase,
      Price(-99.99, laterPrice),
      Price(-99.98, laterPrice),
    };
    await using var fixture = await Fixture.CreateAsync(
      router,
      new Sender(),
      plan
    );
    var policy = (
      await fixture.Planner.BuildAsync(plan, profile, prices, 0, default)
    ).Policy;
    Assert.False(policy.PoorArea);
    Assert.Equal(40, policy.MinimumGallons);
    Assert.Equal(250, policy.TargetGallons);
    var result = FuelOptimizer.Optimize(
      500,
      100,
      profile,
      [
        new(
          purchase.Station,
          200,
          0,
          0,
          purchase.CashUsd,
          purchase.EconomicUsd
        ),
      ],
      1,
      false,
      arrivalPolicy: policy
    );
    var stop = Assert.Single(result.Stops);
    Assert.Equal(full, stop.FillToTarget);
    Assert.Equal(full ? 250 : 100, stop.DepartureGallons);
    Assert.Equal(full ? 190 : 40, result.ArrivalGallons);
    Assert.Equal(0, router.Calls);
  }

  [Fact]
  public async Task NextPickupKeepsExitStationInTheAssignedDirection()
  {
    var id = Guid.NewGuid();
    var nextId = Guid.NewGuid();
    var sender = new Sender
    {
      Rows =
      [
        new()
        {
          Dispatches =
          [
            new() { Id = id },
            new()
            {
              Id = nextId,
              Stops =
              [
                new()
                {
                  Sequence = 1,
                  Latitude = 40,
                  Longitude = -99,
                },
              ],
            },
          ],
        },
      ],
    };
    var plan = new RoutePlan
    {
      DispatchId = id,
      Route = new()
      {
        Miles = 100,
        Legs = [new(100, 6000, [new(40, -101), new(40, -100)])],
      },
    };
    PricedFuelStation Station(string name, double longitude) =>
      new(
        new()
        {
          StationId = Guid.NewGuid(),
          Name = name,
          Point = new(40, longitude),
        },
        3,
        3
      );
    var west = Station("Wrong direction", -100.2);
    var east = Station("Toward pickup", -99.5);
    var router = new Router();
    var profile = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 100,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
    };
    await using var fixture = await Fixture.CreateAsync(router, sender, plan);
    await fixture.SaveConnectionAsync(plan, profile, nextId);
    var planner = fixture.Planner;
    var policy = (
      await planner.BuildAsync(plan, profile, [west, east], 0, default)
    ).Policy;
    Assert.Equal(east.Station.StationId, policy.EscapeStationId);
    Assert.Equal(nextId, policy.NextDispatchId);
    Assert.Equal(0, router.Calls);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task UnverifiedNextPickupDoesNotStartGeocodingOrUseImportedCoordinates(
    bool hasImportedCoordinates
  )
  {
    var id = Guid.NewGuid();
    var pickup = new DispatchStopResponse
    {
      Sequence = 1,
      Address = "100 Main St",
      City = "Town",
      Province = "KS",
      ZipCode = "66000",
      Country = "US",
      Latitude = hasImportedCoordinates ? 40 : null,
      Longitude = hasImportedCoordinates ? -101 : null,
    };
    var sender = new Sender
    {
      Rows =
      [
        new()
        {
          Dispatches =
          [
            new() { Id = id },
            new() { Id = Guid.NewGuid(), Stops = [pickup] },
          ],
        },
      ],
    };
    var router = new Router();
    var plan = new RoutePlan
    {
      DispatchId = id,
      Route = new()
      {
        Miles = 100,
        Legs = [new(100, 6000, [new(40, -101), new(40, -100)])],
      },
    };
    var station = new PricedFuelStation(
      new() { StationId = Guid.NewGuid(), Point = new(40, -99.5) },
      3,
      3
    );
    await using var fixture = await Fixture.CreateAsync(router, sender, plan);
    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Planner.BuildAsync(
          plan,
          new()
          {
            Confirmed = true,
            TankGallons = 100,
            Mpg = 5,
            ReserveGallons = 10,
            FillPercent = 100,
          },
          [station],
          0,
          default
        )
    );
    Assert.Contains("confirmed saved stop location", error.Message);
    Assert.Null(router.Address);
    Assert.Equal(0, router.Calls);
  }

  [Theory]
  [InlineData("matching", true)]
  [InlineData("profile", false)]
  [InlineData("intermediate", false)]
  [InlineData("incomplete", false)]
  [InlineData("missing", false)]
  public async Task OnlyMatchingSavedDeadheadsSupplyOnwardGeometryAndMissingDataNeverRoutes(
    string scenario,
    bool usable
  )
  {
    var id = Guid.NewGuid();
    var nextId = Guid.NewGuid();
    var sender = new Sender
    {
      Rows =
      [
        new()
        {
          Dispatches =
          [
            new() { Id = id },
            new()
            {
              Id = nextId,
              Stops =
              [
                new()
                {
                  Sequence = 1,
                  Latitude = 40,
                  Longitude = -99,
                },
              ],
            },
          ],
        },
      ],
    };
    var plan = new RoutePlan
    {
      DispatchId = id,
      Route = new()
      {
        Miles = 100,
        Legs = [new(100, 6000, [new(40, -101), new(40, -100)])],
      },
    };
    var profile = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 100,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
    };
    var router = new Router();
    await using var fixture = await Fixture.CreateAsync(router, sender, plan);
    var history = await fixture.Services.Deadheads.ReadHistoryAsync(
      [nextId],
      default
    );
    var pair = Assert.IsType<DeadheadConnection>(
      DeadheadConnection.Find(history[nextId])
    );
    var route = new TruckRoute
    {
      Miles = 100,
      Seconds = 6000,
      Legs = [new(100, 6000, [new(40, -100), new(40, -99)])],
    };
    if (scenario == "incomplete")
      route.Legs.Clear();
    if (scenario != "missing")
      fixture.Db.DispatchDeadheads.Add(
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = nextId,
          PreviousDispatchId = id,
          InputHash = pair.Signature(profile),
          Miles = 100,
          RouteJson = JsonSerializer.Serialize(route, RoutingJson.Options),
        }
      );
    if (scenario == "intermediate")
    {
      var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
      fixture.Db.Dispatches.Add(
        new()
        {
          Id = Guid.NewGuid(),
          LoadNumber = 100,
          TruckId = plan.TruckId,
          Status = "assigned",
          ShipDate = date,
          DeliveryDate = date,
          Stops =
          [
            new()
            {
              Id = Guid.NewGuid(),
              Sequence = 1,
              Job = "Pick Up",
              ScheduledDate = date,
              Latitude = 40,
              Longitude = -99.8m,
            },
            new()
            {
              Id = Guid.NewGuid(),
              Sequence = 2,
              Job = "Drop Off",
              ScheduledDate = date,
              Latitude = 40,
              Longitude = -99.7m,
            },
          ],
        }
      );
    }
    await fixture.Db.SaveChangesAsync();
    if (scenario == "profile")
      profile.HeightFeet = 14;
    var station = new PricedFuelStation(
      new() { StationId = Guid.NewGuid(), Point = new(40, -99.5) },
      3,
      3
    );
    if (usable)
      await fixture.Planner.BuildAsync(plan, profile, [station], 0, default);
    else
    {
      var error = await Assert.ThrowsAsync<RoutePlanningException>(
        () => fixture.Planner.BuildAsync(plan, profile, [station], 0, default)
      );
      Assert.Contains("saved connection", error.Message);
    }
    Assert.Equal(0, router.Calls);
    Assert.Null(router.Address);
  }

  [Theory]
  [InlineData(0, true)]
  [InlineData(30, false)]
  public async Task StreetProvenanceMustBeCurrentEvenWhenConnectionGeometryExists(
    int verifiedDaysAgo,
    bool usable
  )
  {
    var id = Guid.NewGuid();
    var nextId = Guid.NewGuid();
    var sender = new Sender
    {
      Rows =
      [
        new()
        {
          Dispatches =
          [
            new() { Id = id },
            new()
            {
              Id = nextId,
              Stops =
              [
                new()
                {
                  Sequence = 1,
                  Address = "100 Main St",
                  Latitude = 40,
                  Longitude = -99,
                },
              ],
            },
          ],
        },
      ],
    };
    var plan = new RoutePlan
    {
      DispatchId = id,
      Route = new()
      {
        Miles = 100,
        Legs = [new(100, 6000, [new(40, -101), new(40, -100)])],
      },
    };
    var profile = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 100,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
    };
    var router = new Router();
    await using var fixture = await Fixture.CreateAsync(router, sender, plan);
    var pickup = await fixture.Db.DispatchStops.SingleAsync(stop =>
      stop.DispatchId == nextId
    );
    pickup.AddressVerifiedAt = DateTime
      .UtcNow.AddDays(-verifiedDaysAgo)
      .AddMinutes(-1);
    pickup.SourceAddressJson = StopAddress.From(pickup).Serialize();
    await fixture.Db.SaveChangesAsync();
    await fixture.SaveConnectionAsync(plan, profile, nextId);
    var station = new PricedFuelStation(
      new() { StationId = Guid.NewGuid(), Point = new(40, -99.5) },
      3,
      3
    );

    if (usable)
      await fixture.Planner.BuildAsync(plan, profile, [station], 0, default);
    else
      await Assert.ThrowsAsync<RoutePlanningException>(
        () => fixture.Planner.BuildAsync(plan, profile, [station], 0, default)
      );

    Assert.Equal(0, router.Calls);
    Assert.Null(router.Address);
  }

  private sealed class Fixture(
    SqliteConnection connection,
    AppDbContext db,
    PlanningTestServices services,
    FuelRegionPlanner planner
  ) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public PlanningTestServices Services => services;
    public FuelRegionPlanner Planner => planner;

    public async Task SaveConnectionAsync(
      RoutePlan plan,
      TruckRouteProfile profile,
      Guid nextId
    )
    {
      var history = await services.Deadheads.ReadHistoryAsync(
        [nextId],
        default
      );
      var pair = Assert.IsType<DeadheadConnection>(
        DeadheadConnection.Find(history[nextId])
      );
      var route = new TruckRoute
      {
        Miles = 100,
        Seconds = 6000,
        Legs =
        [
          new(
            100,
            6000,
            [
              plan.Route.Legs[^1].Points[^1],
              new((double)pair.To.Latitude!, (double)pair.To.Longitude!),
            ]
          ),
        ],
      };
      db.DispatchDeadheads.Add(
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = nextId,
          PreviousDispatchId = plan.DispatchId,
          InputHash = pair.Signature(profile),
          Miles = 100,
          RouteJson = JsonSerializer.Serialize(route, RoutingJson.Options),
        }
      );
      await db.SaveChangesAsync();
    }

    public static async Task<Fixture> CreateAsync(
      Router router,
      Sender sender,
      RoutePlan plan
    )
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .Options
      );
      await db.Database.EnsureCreatedAsync();
      if (plan.TruckId == Guid.Empty)
        plan.TruckId = Guid.NewGuid();
      if (plan.DispatchId == Guid.Empty)
        plan.DispatchId = Guid.NewGuid();
      if (sender.Rows.Count == 0)
        sender.Rows.Add(
          new()
          {
            TruckId = plan.TruckId,
            Dispatches =
            [
              new()
              {
                Id = plan.DispatchId,
                TruckId = plan.TruckId,
                Stops =
                [
                  new()
                  {
                    Sequence = 1,
                    Latitude = (decimal)plan.Route.Legs[0].Points[0].Latitude,
                    Longitude = (decimal)plan.Route.Legs[0].Points[0].Longitude,
                  },
                  new()
                  {
                    Sequence = 2,
                    Latitude = (decimal)plan.Route.Legs[^1].Points[^1].Latitude,
                    Longitude = (decimal)
                      plan.Route.Legs[^1].Points[^1].Longitude,
                  },
                ],
              },
            ],
          }
        );
      db.Trucks.Add(
        new Truck
        {
          Id = plan.TruckId,
          ExternalId = "fuel-region",
          UnitNumber = "Fuel",
          IsActive = true,
        }
      );
      var index = 0;
      foreach (var row in sender.Rows)
      {
        row.TruckId = plan.TruckId;
        foreach (var load in row.Dispatches)
        {
          load.TruckId = plan.TruckId;
          var date = DateOnly
            .FromDateTime(DateTime.UtcNow)
            .AddDays(index++ * 2);
          var stops = load
            .Stops.Select(stop => new DispatchStop
            {
              Id = Guid.NewGuid(),
              Sequence = stop.Sequence,
              TruckId = plan.TruckId,
              Job = stop.Sequence == 1 ? "Pick Up" : "Drop Off",
              Address = stop.Address,
              City = stop.City,
              Province = stop.Province,
              Country = stop.Country,
              ZipCode = stop.ZipCode,
              Latitude = stop.Latitude,
              Longitude = stop.Longitude,
              ScheduledDate = date,
            })
            .ToList();
          if (stops.Count == 0)
            stops =
            [
              new()
              {
                Id = Guid.NewGuid(),
                TruckId = plan.TruckId,
                Sequence = 1,
                Job = "Pick Up",
                ScheduledDate = date,
                Latitude = 40,
                Longitude = -101,
              },
              new()
              {
                Id = Guid.NewGuid(),
                TruckId = plan.TruckId,
                Sequence = 2,
                Job = "Drop Off",
                ScheduledDate = date,
                Latitude = 40,
                Longitude = -100,
              },
            ];
          db.Dispatches.Add(
            new()
            {
              Id = load.Id,
              LoadNumber = index,
              TruckId = plan.TruckId,
              Status = index == 1 ? "in_transit" : "assigned",
              ShipDate = date,
              DeliveryDate = date,
              Stops = stops,
            }
          );
        }
      }
      await db.SaveChangesAsync();
      var services = new PlanningTestServices(db, router, sender);
      return new(
        connection,
        db,
        services,
        new(
          services.FuelInputs,
          Options.Create(new FuelRegionOptions()),
          services.Deadheads,
          new RouteRegionLookup()
        )
      );
    }

    public async ValueTask DisposeAsync()
    {
      services.Dispose();
      await db.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class Router : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls;
    public string? Address;

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      Calls++;
      throw new InvalidOperationException(
        "Fuel region estimates must not request a road."
      );
    }

    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    {
      Address = address;
      throw new InvalidOperationException(
        "Fuel region estimates must not geocode a stop."
      );
    }
  }

  private sealed class Sender : ISender
  {
    public List<TruckDispatchBoardResponse> Rows { get; set; } = [];

    public Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    ) =>
      Task.FromResult(
        (TResponse)
          (object)
            RequestResponse<PaginatedList<TruckDispatchBoardResponse>>.Ok(
              new()
              {
                Items = Rows,
                Page = 1,
                PageSize = 12,
              }
            )
      );

    public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
      where TRequest : IRequest => throw new NotSupportedException();

    public Task<object?> Send(object request, CancellationToken ct = default) =>
      throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
      IStreamRequest<TResponse> request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
      object request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
