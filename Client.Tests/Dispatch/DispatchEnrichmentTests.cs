using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchEnrichmentTests
{
  [Theory]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  public async Task BoardRefreshRetainsForecastsOnlyForSameNativeIdentity(
    bool legChanged,
    bool assignmentChanged
  )
  {
    var clock = new FakeTimeProvider();
    var truck = Guid.NewGuid();
    var loadId = Guid.NewGuid();
    var firstLeg = Guid.NewGuid();
    var nextLeg = legChanged ? Guid.NewGuid() : firstLeg;
    var reads = 0;
    object Board(bool initial) =>
      new
      {
        items = new[]
        {
          new TruckDispatchBoardResponse
          {
            Key = truck.ToString(),
            TruckId = truck,
            TruckNumber = "54777",
            DriverName = "Driver",
            Dispatches =
            [
              new()
              {
                Id = loadId,
                TruckId = truck,
                LoadNumber = 1377,
                Status = "in_transit",
                DriverName = "Driver",
                ExecutionLegId = initial ? firstLeg : nextLeg,
                AssignmentRevision = initial || !assignmentChanged ? 4 : 5,
                TotalMiles = initial ? 900 : null,
                Eta = initial
                  ? new(
                    clock.GetUtcNow().UtcDateTime,
                    clock.GetUtcNow().AddMinutes(2).UtcDateTime,
                    [],
                    "Fixture",
                    []
                  )
                  : null,
              },
            ],
          },
        },
        page = 1,
        totalCount = 1,
      };
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/dispatch/board")
          return Task.FromResult(Ok(Board(++reads == 1)));
        if (path == "/api/fleet/hos")
          return Task.FromResult(Ok(new Dictionary<Guid, TruckHosSnapshot>()));
        return Task.FromResult(Ok(Array.Empty<object>()));
      }
    );
    context.Services.AddSingleton<TimeProvider>(clock);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(
      () => Assert.Single(component.FindComponents<DispatchLoadCard>())
    );
    var original = component.FindComponent<DispatchLoadCard>().Instance;
    Assert.NotNull(original.Load.Eta);
    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(61)));
    component.WaitForAssertion(() => Assert.True(reads >= 2));
    component.WaitForAssertion(() =>
    {
      var current = component.FindComponent<DispatchLoadCard>().Instance;
      if (legChanged || assignmentChanged)
      {
        Assert.NotSame(original, current);
        Assert.Null(current.Load.Eta);
        Assert.Null(current.Load.TotalMiles);
      }
      else
      {
        Assert.Same(original, current);
        Assert.NotNull(current.Load.Eta);
        Assert.Equal(900m, current.Load.TotalMiles);
      }
    });
  }

  [Theory]
  [InlineData(false, false, false, false)]
  [InlineData(true, false, false, false)]
  [InlineData(false, true, false, false)]
  [InlineData(false, false, true, false)]
  [InlineData(false, false, false, true)]
  public async Task CoreAndHosRenderBeforeIndependentEnrichmentAndChangedInputsRejectLateValues(
    bool changed,
    bool driverChanged,
    bool legChanged,
    bool assignmentChanged
  )
  {
    var clock = new FakeTimeProvider();
    var truck = Guid.NewGuid();
    var load = new DispatchResponse
    {
      Id = Guid.NewGuid(),
      TruckId = truck,
      LoadNumber = 1379,
      Status = "in_transit",
      DriverName = "Driver",
      OrderNumber = "ORDER",
      ExecutionLegId = Guid.NewGuid(),
      AssignmentRevision = 4,
    };
    var financial = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var eta = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var planning = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    object Board(DispatchResponse value) =>
      new
      {
        items = new[]
        {
          new TruckDispatchBoardResponse
          {
            Key = truck.ToString(),
            TruckId = truck,
            TruckNumber = "11007",
            DriverName = "Driver",
            Dispatches = [value],
          },
        },
        page = 1,
        totalCount = 1,
      };
    using var context = new ClientComponentContext(
      (request, ct) =>
      {
        var uri = request.RequestUri!;
        if (uri.AbsolutePath == "/api/dispatch/board/enrichment")
        {
          if (uri.Query.Contains("includeFinancials=true"))
            return financial.Task.WaitAsync(ct);
          if (uri.Query.Contains("includeEta=true"))
            return eta.Task.WaitAsync(ct);
          throw new InvalidOperationException(
            "An enrichment kind is required."
          );
        }
        if (uri.AbsolutePath == "/api/dispatch/board")
        {
          Assert.Contains(
            "includeFinancials=false&includeEta=false",
            uri.Query
          );
          return Task.FromResult(Ok(Board(load)));
        }
        if (uri.AbsolutePath == "/api/dispatch/board/planning")
          return planning.Task.WaitAsync(ct);
        if (uri.AbsolutePath == "/api/fleet/hos")
          return Task.FromResult(
            Ok(
              new Dictionary<Guid, TruckHosSnapshot>
              {
                [truck] = new(
                  driverChanged ? "Replacement" : "Driver",
                  new()
                  {
                    UpdatedAt = clock.GetUtcNow().UtcDateTime,
                    DriveMs = 7200000,
                  }
                ),
              }
            )
          );
        return Task.FromResult(Ok(Array.Empty<object>()));
      }
    );
    context.Services.AddSingleton<TimeProvider>(clock);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(() =>
    {
      Assert.Single(component.FindComponents<DispatchLoadCard>());
      Assert.Contains("2:00", component.Find(".driver-hours").TextContent);
    });
    var card = component.FindComponent<DispatchLoadCard>().Instance;
    Assert.False(financial.Task.IsCompleted);
    Assert.False(eta.Task.IsCompleted);
    var enriched = new DispatchResponse
    {
      Id = load.Id,
      TruckId = truck,
      TotalMiles = 900,
      PlanningAssignmentRevision = changed ? 1 : 0,
      ExecutionLegId = legChanged ? Guid.NewGuid() : load.ExecutionLegId,
      AssignmentRevision = assignmentChanged ? 5 : load.AssignmentRevision,
    };
    object Extras(bool money) =>
      new[]
      {
        new TruckDispatchEnrichment(
          truck.ToString(),
          truck,
          "Driver",
          null,
          [
            new(
              enriched.Id,
              truck,
              enriched.LastSyncedAt,
              enriched.PlanningAssignmentRevision,
              enriched.RouteChoiceRevision,
              [],
              money ? new(null, 900, null, null, "available") : null,
              money ? null : enriched.Eta,
              enriched.ExecutionLegId,
              enriched.AssignmentRevision
            ),
          ]
        ),
      };
    financial.SetResult(Ok(Extras(true)));
    var inputsChanged = changed || legChanged || assignmentChanged;
    component.WaitForAssertion(
      () => Assert.Equal(inputsChanged ? null : 900m, card.Load.TotalMiles)
    );
    Assert.Null(card.Load.Eta);
    enriched.Eta = new(
      clock.GetUtcNow().UtcDateTime,
      clock.GetUtcNow().AddMinutes(2).UtcDateTime,
      [],
      "Fixture",
      []
    );
    eta.SetResult(Ok(Extras(false)));
    planning.SetResult(Ok(Array.Empty<AutomaticPlanningResult>()));
    component.WaitForAssertion(
      () =>
        Assert.False(
          component.FindComponent<DispatchLoadCard>().Instance.Refreshing
        )
    );
    Assert.Equal(inputsChanged || driverChanged, card.Load.Eta is null);
    Assert.Same(card, component.FindComponent<DispatchLoadCard>().Instance);
  }

  private static HttpResponseMessage Ok(object value) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(new { success = true, response = value }),
    };
}
