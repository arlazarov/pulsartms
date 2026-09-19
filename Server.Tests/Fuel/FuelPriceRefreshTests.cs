using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using MediatR;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelPriceRefreshTests
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ChangedPricingProfileRefreshesWithoutWaitingForNewQuotes(
    bool exchangeRate
  )
  {
    var store = new Store();
    var sender = new Sender { Loads = store.Loads };
    var current = Current(store);
    if (exchangeRate)
      current.State!.Profile.CadToUsd = .72;
    else
      current.State!.Profile.UseIfta = !current.State.Profile.UseIfta;
    var service = new FuelPriceRefreshService(
      store,
      sender,
      sender,
      new CarrierFuelPrices(sender),
      TimeProvider.System,
      sender
    );

    await service.RefreshAsync(current, default);

    var command = Assert.Single(sender.Calculations);
    Assert.Equal(current.DispatchId, command.DispatchId);
    Assert.Equal(current.ExecutionLegId, command.ExecutionLegId);
    Assert.Equal(current.AssignmentRevision, command.AssignmentRevision);
    Assert.Equal(0, sender.PriceReads);
    Assert.Equal(0, store.Writes);
  }

  [Fact]
  public async Task CachedQuotesFollowIftaAndExchangeRateWithoutSharingMutableStops()
  {
    var sender = new Sender();
    var station = sender.Prices[0];
    sender.Prices[0] = station with
    {
      Discounts =
      [
        station.Discounts[0] with
        {
          Currency = "CAD",
          Unit = "L",
          PriceAfterIfta = 2,
        },
      ],
    };
    var calendar = new FuelPriceCalendar(
      new CarrierFuelPrices(sender),
      new(2026, 9, 14),
      sender.Prices
    );
    var candidate = new FuelCandidate(
      new() { StationId = station.Id },
      100,
      1,
      1,
      3,
      3
    );
    var profile = new TruckRouteProfile { UseIfta = false, CadToUsd = .7 };
    var first = Assert.Single(
      await calendar.PriceAsync(
        [candidate],
        new Dictionary<string, DateTimeOffset>(),
        profile,
        default
      )
    );
    first.Station.YourPrice = 99;
    var repeated = Assert.Single(
      await calendar.PriceAsync(
        [candidate],
        new Dictionary<string, DateTimeOffset>(),
        profile,
        default
      )
    );
    Assert.Equal(3, repeated.Station.YourPrice);
    Assert.NotSame(first.Station, repeated.Station);
    profile.UseIfta = true;
    profile.CadToUsd = .8;
    var changed = Assert.Single(
      await calendar.PriceAsync(
        [candidate],
        new Dictionary<string, DateTimeOffset>(),
        profile,
        default
      )
    );
    Assert.Equal(3 * 3.785411784 * .8, changed.PriceUsd, 8);
    Assert.Equal(2 * 3.785411784 * .8, changed.EconomicPriceUsd, 8);
    Assert.Equal(3 * 3.785411784 * .7, repeated.EconomicPriceUsd, 8);
    Assert.Equal(0, sender.PriceReads);
  }

  [Fact]
  public async Task CancelledCalendarDoesNotReadOrConvertPrices()
  {
    var sender = new Sender();
    var calendar = new FuelPriceCalendar(
      new CarrierFuelPrices(sender),
      new(2026, 9, 14),
      sender.Prices
    );
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () =>
        calendar.PriceAsync(
          [],
          new Dictionary<string, DateTimeOffset>(),
          new(),
          cancellation.Token
        )
    );
    Assert.Equal(0, sender.PriceReads);
  }

  [Fact]
  public async Task ArrivalLocalDateChoosesFuturePriceAndMissingQuoteFallsBackToToday()
  {
    var sender = new Sender();
    var today = new DateOnly(2026, 9, 14);
    var tomorrow = today.AddDays(1);
    var station = sender.Prices.Single();
    var quote = station.Discounts.Single();
    sender.Days[tomorrow] =
    [
      station with
      {
        Discounts =
        [
          quote with
          {
            EffectiveFrom = tomorrow,
            DiscountPrice = 2,
          },
        ],
      },
    ];
    var candidate = new FuelCandidate(
      new() { StationId = station.Id },
      100,
      1,
      1,
      3,
      3
    )
    {
      LegIndex = 0,
    };
    var calendar = new FuelPriceCalendar(
      new CarrierFuelPrices(sender),
      today,
      sender.Prices
    );
    var at = new DateTimeOffset(2026, 9, 15, 0, 30, 0, TimeSpan.FromHours(-7));
    var dates = new Dictionary<string, DateTimeOffset>
    {
      [candidate.VisitKey] = at,
    };
    var priced = Assert.Single(
      await calendar.PriceAsync(
        [candidate],
        dates,
        new() { UseIfta = false },
        default
      )
    );
    Assert.Equal(2, priced.PriceUsd);
    Assert.Equal(tomorrow, priced.Station.PriceDate);
    Assert.False(priced.Station.PriceEstimated);
    Assert.Equal(at, priced.Station.EstimatedArrival);
    await calendar.PriceAsync(
      [candidate],
      dates,
      new() { UseIfta = false },
      default
    );
    Assert.Equal(1, sender.PriceReads);
    dates[candidate.VisitKey] = at.AddDays(3);
    sender.Days[tomorrow.AddDays(3)] = [];
    var fallback = Assert.Single(
      await calendar.PriceAsync(
        [candidate],
        dates,
        new() { UseIfta = false },
        default
      )
    );
    Assert.Equal(3, fallback.PriceUsd);
    Assert.True(fallback.Station.PriceEstimated);
    Assert.Equal(2, priced.PriceUsd);
  }

  [Fact]
  public async Task NewlyPublishedTomorrowPriceRefreshesBeforeItBecomesEffectiveToday()
  {
    var store = new Store();
    var sender = new Sender { Loads = store.Loads };
    var today = new DateOnly(2026, 9, 14);
    var tomorrow = today.AddDays(1);
    store.Snapshot.Plan.PriceDates = [today, tomorrow];
    sender.Days[tomorrow] = [];
    store.Snapshot.Plan.UsDiscountSignature = UsFuelDiscountSignature.Calendar(
      new Dictionary<DateOnly, List<FuelStationDto>>
      {
        [today] = sender.Prices,
        [tomorrow] = [],
      }
    );
    var service = new FuelPriceRefreshService(
      store,
      sender,
      sender,
      new CarrierFuelPrices(sender),
      new ManualTimeProvider(
        new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero)
      ),
      sender
    );
    await service.RefreshAsync(Current(store), default);
    Assert.Empty(sender.Calculations);
    sender.Days[tomorrow] =
    [
      sender.Prices[0] with
      {
        Discounts =
        [
          sender.Prices[0].Discounts[0] with
          {
            EffectiveFrom = tomorrow,
          },
        ],
      },
    ];
    await service.RefreshAsync(Current(store), default);
    Assert.Single(sender.Calculations);
  }

  [Fact]
  public async Task ChangedPricesRecalculateFailedWorkRetriesAndCommittedPricesDoNotRepeat()
  {
    var store = new Store();
    var sender = new Sender { Loads = store.Loads, FailCalculations = true };
    var clock = new ManualTimeProvider(
      new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero)
    );
    var current = Current(store);
    var service = new FuelPriceRefreshService(
      store,
      sender,
      sender,
      new CarrierFuelPrices(sender),
      clock,
      sender
    );
    var first = await Assert.ThrowsAsync<RoutePlanningException>(
      () => service.RefreshAsync(current, default)
    );
    Assert.Equal("GPS unavailable.", first.Message);
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => service.RefreshAsync(current, default)
    );
    Assert.Equal(2, sender.Calculations.Count);
    Assert.All(
      sender.Calculations,
      command =>
      {
        Assert.Equal(current.DispatchId, command.DispatchId);
        Assert.Equal(current.ExecutionLegId, command.ExecutionLegId);
        Assert.Equal(current.AssignmentRevision, command.AssignmentRevision);
      }
    );
    store.Snapshot.Plan.UsDiscountSignature = UsFuelDiscountSignature.Calendar(
      new Dictionary<DateOnly, List<FuelStationDto>>
      {
        [new(2026, 9, 14)] = sender.Prices,
      }
    );
    await new FuelPriceRefreshService(
      store,
      sender,
      sender,
      new CarrierFuelPrices(sender),
      clock,
      sender
    ).RefreshAsync(current, default);
    Assert.Equal(2, sender.Calculations.Count);
    Assert.Equal(0, store.Writes);
  }

  [Theory]
  [InlineData("manual")]
  [InlineData("starting-fuel")]
  [InlineData("completed")]
  [InlineData("stale-route")]
  public async Task IneligiblePlansAreNotReplaced(string scenario)
  {
    var store = new Store();
    var sender = new Sender { Loads = store.Loads };
    var current = Current(store);
    switch (scenario)
    {
      case "manual":
        store.Snapshot.Plan.ManuallyEdited = true;
        break;
      case "starting-fuel":
        store.Snapshot.Plan.ManualStartingFuel = true;
        break;
      case "other-load":
        current = current with { DispatchId = Guid.NewGuid() };
        break;
      case "other-leg":
        current = current with { ExecutionLegId = Guid.NewGuid() };
        break;
      case "revision":
        current = current with { AssignmentRevision = 2 };
        break;
      case "completed":
        current.State!.Plan!.Tracking.AllStopsPassed = true;
        break;
      case "stale-route":
        current.State!.Plan!.InputsChanged = true;
        break;
    }
    await new FuelPriceRefreshService(
      store,
      sender,
      sender,
      new CarrierFuelPrices(sender),
      TimeProvider.System,
      sender
    ).RefreshAsync(current, default);
    Assert.Empty(sender.Calculations);
    Assert.Equal(0, sender.PriceReads);
  }

  [Fact]
  public async Task ChangedUpcomingAssignmentsRefreshWithoutChangedPrices()
  {
    var store = new Store();
    var sender = new Sender { Loads = store.Loads };
    var clock = new ManualTimeProvider(
      new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero)
    );
    store.Snapshot.Plan.UsDiscountSignature = UsFuelDiscountSignature.Calendar(
      new Dictionary<DateOnly, List<FuelStationDto>>
      {
        [new(2026, 9, 14)] = sender.Prices,
      }
    );
    var current = Current(store);
    var service = new FuelPriceRefreshService(
      store,
      sender,
      sender,
      new CarrierFuelPrices(sender),
      clock,
      sender
    );
    await service.RefreshAsync(current, default);
    Assert.Empty(sender.Calculations);

    var next = new DispatchResponse
    {
      Id = Guid.NewGuid(),
      TruckId = store.Snapshot.TruckId,
      Status = "assigned",
      Stops =
      [
        new() { Id = Guid.NewGuid(), Job = "Pick Up" },
        new()
        {
          Id = Guid.NewGuid(),
          Job = "Drop Off",
          Sequence = 1,
        },
      ],
    };
    DispatchProjection.Complete(next);
    store.Loads.Add(next);
    await service.RefreshAsync(current, default);
    Assert.Single(sender.Calculations);

    store.Snapshot.Plan.DispatchIds.Add(next.Id);
    store.Snapshot.Plan.DispatchSignatures[next.Id] = FuelHorizon.LoadSignature(
      next
    );
    await service.RefreshAsync(current, default);
    Assert.Single(sender.Calculations);

    next.Stops[1].Address = "200 Updated Street";
    await service.RefreshAsync(current, default);
    Assert.Equal(2, sender.Calculations.Count);
    store.Loads.Remove(next);
    await service.RefreshAsync(current, default);
    Assert.Equal(3, sender.Calculations.Count);
    store.Snapshot.Plan.ManuallyEdited = true;
    await service.RefreshAsync(current, default);
    Assert.Equal(3, sender.Calculations.Count);
    Assert.Equal(0, store.Writes);
  }

  [Theory]
  [InlineData("next-load")]
  [InlineData("next-execution")]
  [InlineData("assignment-revision")]
  [InlineData("selection-version")]
  public async Task AutomaticPlanFollowsTheCurrentPlanningScope(string scenario)
  {
    var store = new Store();
    var sender = new Sender { Loads = store.Loads };
    var current = Current(store);
    switch (scenario)
    {
      case "next-load":
        current = current with { DispatchId = Guid.NewGuid() };
        break;
      case "next-execution":
        current = current with { ExecutionLegId = Guid.NewGuid() };
        break;
      case "assignment-revision":
        current = current with { AssignmentRevision = 2 };
        break;
      case "selection-version":
        store.Snapshot.Plan.SelectionVersion--;
        break;
    }

    await new FuelPriceRefreshService(
      store,
      sender,
      sender,
      new CarrierFuelPrices(sender),
      TimeProvider.System,
      sender
    ).RefreshAsync(current, default);

    var command = Assert.Single(sender.Calculations);
    Assert.Equal(current.DispatchId, command.DispatchId);
    Assert.Equal(current.ExecutionLegId, command.ExecutionLegId);
    Assert.Equal(current.AssignmentRevision, command.AssignmentRevision);
    Assert.Equal(0, sender.PriceReads);
  }

  [Theory]
  [InlineData("changed")]
  [InlineData("missing")]
  [InlineData("unsupported")]
  public async Task RoadChangesRefreshWithoutWaitingForNewPrices(string change)
  {
    var store = new Store();
    var sender = new Sender { Loads = store.Loads, RoadsMatch = false };
    if (change != "changed")
      store.Snapshot = store.Snapshot with
      {
        RoadDependencies =
          change == "missing"
            ? null
            : store.Snapshot.RoadDependencies! with
            {
              Version = 2,
            },
      };

    await new FuelPriceRefreshService(
      store,
      sender,
      sender,
      new CarrierFuelPrices(sender),
      TimeProvider.System,
      sender
    ).RefreshAsync(Current(store), default);

    var command = Assert.Single(sender.Calculations);
    Assert.Equal(store.Snapshot.RootDispatchId, command.DispatchId);
    Assert.Equal(0, sender.PriceReads);
    Assert.Equal(0, store.Writes);
    Assert.Equal(change == "changed" ? 1 : 0, sender.RoadReads);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task RoadChangesNeverAutomaticallyReplaceManualChoices(
    bool startingFuel
  )
  {
    var store = new Store();
    store.Snapshot.Plan.ManuallyEdited = !startingFuel;
    store.Snapshot.Plan.ManualStartingFuel = startingFuel;
    var sender = new Sender { Loads = store.Loads, RoadsMatch = false };

    await new FuelPriceRefreshService(
      store,
      sender,
      sender,
      new CarrierFuelPrices(sender),
      TimeProvider.System,
      sender
    ).RefreshAsync(Current(store), default);

    Assert.Empty(sender.Calculations);
    Assert.Equal(0, sender.RoadReads);
    Assert.Equal(0, sender.PriceReads);
    Assert.Equal(0, store.Writes);
  }

  [Fact]
  public async Task FailedRoadRefreshKeepsTheRevisionForRetry()
  {
    var store = new Store();
    var sender = new Sender
    {
      Loads = store.Loads,
      RoadsMatch = false,
      FailCalculations = true,
    };
    var service = new FuelPriceRefreshService(
      store,
      sender,
      sender,
      new CarrierFuelPrices(sender),
      TimeProvider.System,
      sender
    );
    for (var attempt = 0; attempt < 2; attempt++)
      await Assert.ThrowsAsync<RoutePlanningException>(
        () => service.RefreshAsync(Current(store), default)
      );

    Assert.Equal(2, sender.Calculations.Count);
    Assert.All(
      sender.Calculations,
      command => Assert.Equal(store.Snapshot.RootDispatchId, command.DispatchId)
    );
    Assert.Equal(0, store.Writes);
    Assert.Equal(0, sender.PriceReads);
  }

  private static AutomaticPlanningResult Current(Store store) =>
    new(
      store.Snapshot.TruckId,
      store.Snapshot.RootDispatchId,
      1,
      new(
        new(),
        new()
        {
          TruckId = store.Snapshot.TruckId,
          DispatchId = store.Snapshot.RootDispatchId,
          ExecutionLegId = store.Snapshot.RootExecutionLegId,
          AssignmentRevision = store.Snapshot.AssignmentRevision,
        },
        null,
        50,
        DateTime.UtcNow,
        true
      ),
      null
    )
    {
      ExecutionLegId = store.Snapshot.RootExecutionLegId,
      AssignmentRevision = 1,
    };

  private sealed class Store : ITruckFuelPlanStore
  {
    public Store()
    {
      Loads =
      [
        new()
        {
          Id = Snapshot.RootDispatchId,
          TruckId = Snapshot.TruckId,
          ExecutionLegId = Snapshot.RootExecutionLegId,
          AssignmentRevision = Snapshot.AssignmentRevision,
          ExecutionStatus = "active",
          Status = "in_transit",
          Stops = [new() { Id = Guid.NewGuid(), Job = "Drop Off" }],
        },
      ];
      Snapshot = Snapshot with
      {
        RoadDependencies = FuelRoadDependencies.Capture(
          [
            new(
              new(Snapshot.RootDispatchId, Snapshot.RootExecutionLegId),
              SavedRoadKind.Plan,
              new string('A', 64)
            ),
          ]
        ),
      };
      Snapshot.Plan.TruckId = Snapshot.TruckId;
      Snapshot.Plan.ExecutionLegId = Snapshot.RootExecutionLegId;
      Snapshot.Plan.AssignmentRevision = Snapshot.AssignmentRevision;
      Snapshot.Plan.DispatchIds = [Snapshot.RootDispatchId];
      Snapshot.Plan.DispatchSignatures[Snapshot.RootDispatchId] =
        FuelHorizon.LoadSignature(Loads[0]);
      Snapshot.Plan.ProfileSignature = JsonSerializer.Serialize(
        new TruckRouteProfile(),
        RoutePlanningService.Json
      );
    }

    public List<DispatchResponse> Loads { get; }
    public TruckFuelPlanSnapshot Snapshot { get; set; } =
      new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        DateTime.UtcNow,
        new() { SelectionVersion = FuelOptimizer.SelectionVersion },
        [],
        null
      )
      {
        RootExecutionLegId = Guid.NewGuid(),
        AssignmentRevision = 1,
      };
    public int Writes { get; private set; }

    public Task<TruckFuelPlanSnapshot?> ReadAsync(
      Guid id,
      bool includeRoute,
      CancellationToken ct
    )
    {
      Assert.False(includeRoute);
      return Task.FromResult<TruckFuelPlanSnapshot?>(Snapshot);
    }

    public Task<bool> SaveAsync(
      TruckFuelPlanSnapshot snapshot,
      CancellationToken ct
    )
    {
      Writes++;
      return Task.FromResult(true);
    }

    public Task<bool> ReplaceAsync(
      TruckFuelPlanSnapshot snapshot,
      DateTime? expected,
      CancellationToken ct
    )
    {
      Writes++;
      return Task.FromResult(true);
    }
  }

  private sealed class Sender
    : ISender,
      IFuelWorkInputsReader,
      IFuelSavedInputsValidation
  {
    public List<DispatchResponse> Loads { get; init; } = [];
    public bool FailCalculations { get; init; }
    public bool RoadsMatch { get; set; } = true;
    public int RoadReads { get; private set; }

    public Task<bool> MatchesAsync(
      FuelRecommendations access,
      CancellationToken ct
    )
    {
      ct.ThrowIfCancellationRequested();
      RoadReads++;
      return Task.FromResult(RoadsMatch);
    }

    public Task<bool> MatchesAsync(
      TruckFuelPlanSnapshot saved,
      RoutePlan current,
      CancellationToken ct
    )
    {
      ct.ThrowIfCancellationRequested();
      if (FuelRoadDependencies.Remaining(saved, current) is null)
        return Task.FromResult(false);
      RoadReads++;
      return Task.FromResult(RoadsMatch);
    }

    public List<FuelStationDto> Prices { get; } =
      [UsFuelDiscountSignatureTests.Station()];
    public List<RecalculateFuelPlanCommand> Calculations { get; } = [];
    public int PriceReads { get; private set; }
    public Dictionary<DateOnly, List<FuelStationDto>> Days { get; } = [];

    public Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    )
    {
      object response;
      if (request is GetFuelStationsQuery query)
      {
        PriceReads++;
        response = RequestResponse<List<FuelStationDto>>.Ok(
          (query.Date is { } day ? Days.GetValueOrDefault(day) : null) ?? Prices
        );
      }
      else
      {
        Calculations.Add(Assert.IsType<RecalculateFuelPlanCommand>(request));
        response = FailCalculations
          ? RequestResponse<AutomaticPlanningResult>.Fail("GPS unavailable.")
          : RequestResponse<AutomaticPlanningResult>.Ok(
            new(Guid.Empty, null, null, null, null)
          );
      }
      return Task.FromResult((TResponse)response);
    }

    public Task<FuelWorkInputs> ReadFreshAsync(
      Guid truckId,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<FuelWorkInputs?> ReadDisplayAsync(
      Guid truckId,
      CancellationToken ct
    )
    {
      ct.ThrowIfCancellationRequested();
      return Task.FromResult<FuelWorkInputs?>(
        FuelWorkFixture.Capture(truckId, Loads)
      );
    }

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
