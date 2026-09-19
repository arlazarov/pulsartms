using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Services;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchForecastPublishingTests
{
  private static readonly DateTimeOffset Start = new(
    2026,
    9,
    8,
    12,
    0,
    0,
    TimeSpan.Zero
  );

  [Fact]
  public async Task ExistingPlanningReadUpdatesBothLoadsBeforeTheBoardPollAndOlderBoardCannotReplaceIt()
  {
    var truckId = Guid.NewGuid();
    var loads = Loads(truckId);
    var clock = new FakeTimeProvider(Start);
    var boardReads = 0;
    var planningReads = 0;
    var planningStarted = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var planningResponse = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.RequestUri!.AbsolutePath == "/api/dispatch/board")
        {
          if (
            request.RequestUri.Query.Contains("includeEta=true")
            || request.RequestUri.Query.Contains("includeFinancials=true")
          )
            return Task.FromResult(Board(truckId, loads));
          boardReads++;
          if (boardReads > 1)
            foreach (var load in loads)
              load.Eta = Forecast([load], Start.AddMinutes(-1));
          return Task.FromResult(Board(truckId, loads));
        }
        if (request.RequestUri.AbsolutePath == "/api/dispatch/board/planning")
        {
          planningReads++;
          planningStarted.TrySetResult();
          return planningResponse.Task;
        }
        return Task.FromResult(Auxiliary(request.RequestUri));
      }
    );
    context.Services.AddSingleton<TimeProvider>(clock);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    await planningStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    component.WaitForAssertion(
      () =>
        Assert.Equal(4, component.FindAll(".dispatch-load__eta-missing").Count)
    );
    var forecast = Forecast(loads, Start);
    planningResponse.SetResult(
      Json(new[] { Result(truckId, loads[0].Id, forecast) })
    );
    component.WaitForAssertion(() =>
    {
      var cards = component.FindComponents<DispatchLoadCard>();
      Assert.Equal(2, cards.Count);
      Assert.All(
        cards,
        card =>
        {
          Assert.Equal(Start.UtcDateTime, card.Instance.Load.Eta!.CalculatedAt);
          Assert.Equal(2, card.Instance.Load.Eta.Stops.Count);
          Assert.All(
            card.Instance.Load.Eta.Stops,
            stop => Assert.Equal(card.Instance.Load.Id, stop.DispatchId)
          );
        }
      );
    });
    Assert.Equal(1, boardReads);
    Assert.Equal(1, planningReads);

    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(61)));
    component.WaitForAssertion(() => Assert.Equal(2, boardReads));
    component.WaitForAssertion(
      () =>
        Assert.All(
          component.FindComponents<DispatchLoadCard>(),
          card =>
            Assert.Equal(
              Start.UtcDateTime,
              card.Instance.Load.Eta!.CalculatedAt
            )
        )
    );
    var callback = component
      .FindComponent<DispatchPlanning>()
      .Instance.ForecastChanged;
    await component.InvokeAsync(
      () => callback.InvokeAsync(Forecast(loads, Start.AddMinutes(-1)))
    );
    Assert.All(
      component.FindComponents<DispatchLoadCard>(),
      card =>
        Assert.Equal(Start.UtcDateTime, card.Instance.Load.Eta!.CalculatedAt)
    );
    Assert.Equal(2, planningReads);
  }

  [Fact]
  public async Task ReassignedTruckRejectsThePreviousTrucksQueuedForecastAndBoardRetention()
  {
    var oldTruck = Guid.NewGuid();
    var newTruck = Guid.NewGuid();
    var assignedTruck = oldTruck;
    var loads = Loads(oldTruck);
    var planningStarted = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.RequestUri!.AbsolutePath == "/api/dispatch/board")
          return Task.FromResult(Board(assignedTruck, loads));
        if (
          request.RequestUri.AbsolutePath.EndsWith(
            "/planning",
            StringComparison.Ordinal
          )
        )
        {
          planningStarted.TrySetResult();
          return Task.FromResult(
            Json(
              new[]
              {
                Result(
                  assignedTruck,
                  loads[0].Id,
                  assignedTruck == oldTruck ? Forecast(loads, Start) : null
                ),
              }
            )
          );
        }
        return Task.FromResult(Auxiliary(request.RequestUri));
      }
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    await planningStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    component.WaitForAssertion(
      () =>
      {
        var cards = component.FindComponents<DispatchLoadCard>();
        Assert.Equal(2, cards.Count);
        Assert.All(cards, card => Assert.NotNull(card.Instance.Load.Eta));
      },
      TimeSpan.FromSeconds(5)
    );
    var oldCallback = component
      .FindComponent<DispatchPlanning>()
      .Instance.ForecastChanged;
    assignedTruck = newTruck;
    foreach (var load in loads)
      load.TruckId = newTruck;
    await component.InvokeAsync(
      () =>
        context
          .Services.GetRequiredService<NavigationManager>()
          .NavigateTo($"/dispatch?truckId={newTruck}")
    );
    component.WaitForAssertion(
      () =>
        Assert.Equal(4, component.FindAll(".dispatch-load__eta-missing").Count)
    );

    await component.InvokeAsync(
      () => oldCallback.InvokeAsync(Forecast(loads, Start.AddSeconds(1)))
    );
    Assert.All(
      component.FindComponents<DispatchLoadCard>(),
      card => Assert.Null(card.Instance.Load.Eta)
    );
  }

  [Fact]
  public async Task ChangedFutureRouteKeepsItsPreviousPresentationWhileItsNewForecastIsPending()
  {
    var truckId = Guid.NewGuid();
    var loads = Loads(truckId);
    var planningStarted = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var planningResponse = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.RequestUri!.AbsolutePath == "/api/dispatch/board")
          return Task.FromResult(Board(truckId, loads));
        if (
          request.RequestUri.AbsolutePath.EndsWith(
            "/planning",
            StringComparison.Ordinal
          )
        )
        {
          planningStarted.TrySetResult();
          return planningResponse.Task;
        }
        return Task.FromResult(Auxiliary(request.RequestUri));
      }
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    await planningStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    planningResponse.SetResult(
      Json(new[] { Result(truckId, loads[0].Id, Forecast(loads, Start)) })
    );
    component.WaitForAssertion(
      () =>
      {
        var ready = component.FindComponents<DispatchLoadCard>();
        Assert.Equal(2, ready.Count);
        Assert.All(ready, card => Assert.NotNull(card.Instance.Load.Eta));
      },
      TimeSpan.FromSeconds(5)
    );
    var previousMarkup = component.FindComponents<DispatchLoadCard>()[1].Markup;
    var forecast = Forecast([loads[0]], Start.AddSeconds(1)) with
    {
      PendingDispatches = new Dictionary<Guid, string>
      {
        [loads[1].Id] = "Saved connection changed.",
      },
    };
    var callback = component
      .FindComponent<DispatchPlanning>()
      .Instance.ForecastChanged;
    await component.InvokeAsync(() => callback.InvokeAsync(forecast));
    var cards = component.FindComponents<DispatchLoadCard>();
    Assert.NotEmpty(cards[0].Instance.Load.Eta!.Stops);
    Assert.Empty(cards[1].Instance.Load.Eta!.Stops);
    Assert.True(cards[1].Instance.Load.Eta!.RouteUpdatePending);
    Assert.Equal(previousMarkup, cards[1].Markup);
    Assert.Equal(2, cards[1].FindAll(".arrival-estimate__ontime").Count);
    Assert.DoesNotContain("Updating", cards[1].Markup);
  }

  [Fact]
  public async Task FreshCachedForecastIsPublishedWhileTheExistingRefreshIsStillPending()
  {
    var truckId = Guid.NewGuid();
    var load = Loads(truckId)[0];
    var cached = Forecast([load], Start);
    var refreshed = Forecast([load], Start.AddSeconds(1));
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var reads = 0;
    using var context = new ClientComponentContext(
      (_, _) =>
      {
        reads++;
        return pending.Task;
      }
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    context
      .Services.GetRequiredService<PlanningDisplayCache>()
      .Store(
        $"api/fleet/trucks/{truckId}/planning",
        Result(truckId, load.Id, cached)
      );
    var published = new List<DispatchEta>();
    var component = context.Render<DispatchPlanning>(parameters =>
      parameters
        .Add(part => part.TruckId, truckId)
        .Add(part => part.Load, load)
        .Add(
          part => part.ForecastChanged,
          (DispatchEta value) => published.Add(value)
        )
    );
    Assert.Same(cached, Assert.Single(published));
    Assert.Equal(1, reads);
    pending.SetResult(Json(Result(truckId, load.Id, refreshed)));
    component.WaitForAssertion(() => Assert.Equal(2, published.Count));
    Assert.Equal(refreshed.CalculatedAt, published[1].CalculatedAt);
    Assert.Equal(1, reads);
  }

  [Theory]
  [InlineData("truck")]
  [InlineData("load")]
  [InlineData("expired")]
  public void ExpiredOrDifferentAssignmentForecastsAreNotPublished(
    string mismatch
  )
  {
    var truckId = Guid.NewGuid();
    var load = Loads(truckId)[0];
    var forecast = Forecast(
      [load],
      mismatch == "expired" ? Start.AddMinutes(-3) : Start
    );
    var result = Result(
      mismatch == "truck" ? Guid.NewGuid() : truckId,
      mismatch == "load" ? Guid.NewGuid() : load.Id,
      forecast
    );
    using var context = new ClientComponentContext(
      (_, _) => Task.FromResult(Json(result))
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    var published = new List<DispatchEta>();
    var component = context.Render<DispatchPlanning>(parameters =>
      parameters
        .Add(part => part.TruckId, truckId)
        .Add(part => part.Load, load)
        .Add(
          part => part.ForecastChanged,
          (DispatchEta value) => published.Add(value)
        )
    );
    component.WaitForAssertion(
      () => Assert.DoesNotContain("Loading saved route", component.Markup)
    );
    Assert.Empty(published);
  }

  private static DispatchResponse[] Loads(Guid truckId) =>
    Enumerable
      .Range(1, 2)
      .Select(number => new DispatchResponse
      {
        Id = Guid.NewGuid(),
        TruckId = truckId,
        LoadNumber = number,
        Status = number == 1 ? "in_transit" : "planned",
        Stops = Enumerable
          .Range(1, 2)
          .Select(sequence => new DispatchStopResponse
          {
            Id = Guid.NewGuid(),
            Sequence = sequence,
            Job = sequence == 1 ? "Pickup" : "Delivery",
            City = $"Stop {number}/{sequence}",
            ScheduledDate = new(2026, 9, 8),
          })
          .ToList(),
      })
      .ToArray();

  private static DispatchEta Forecast(
    IEnumerable<DispatchResponse> loads,
    DateTimeOffset at
  ) =>
    new(
      at.UtcDateTime,
      at.AddMinutes(2).UtcDateTime,
      loads
        .SelectMany(load =>
          load.Stops.Select(stop => new StopEta(
            stop.Id,
            at.AddHours(stop.Sequence),
            "Etc/UTC",
            null,
            0,
            60,
            0
          )
          {
            DispatchId = load.Id,
          })
        )
        .ToArray(),
      null,
      []
    );

  private static AutomaticPlanningResult Result(
    Guid truckId,
    Guid loadId,
    DispatchEta? eta
  ) =>
    new(
      truckId,
      loadId,
      1,
      new(
        new(),
        new()
        {
          Id = loadId,
          TruckId = truckId,
          DispatchId = loadId,
          Version = 1,
        },
        null,
        null,
        null,
        true
      )
      {
        Eta = eta,
      },
      null
    );

  private static HttpResponseMessage Board(
    Guid truckId,
    DispatchResponse[] loads
  ) =>
    Json(
      new
      {
        items = new[]
        {
          new
          {
            key = truckId.ToString(),
            truckId,
            truckNumber = "11006",
            dispatches = loads,
          },
        },
        page = 1,
        pageSize = 20,
        totalCount = 1,
        totalPages = 1,
      }
    );

  private static HttpResponseMessage Auxiliary(Uri uri) =>
    uri.AbsolutePath == "/api/fleet/locations"
      ? Json(
        new { trucks = Array.Empty<object>(), points = Array.Empty<object>() }
      )
      : Json(Array.Empty<object>());

  private static HttpResponseMessage Json(object value) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(new { success = true, response = value }),
    };
}
