using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchBatchRefreshTests
{
  private static readonly DateTimeOffset Start = new(
    2026,
    9,
    12,
    12,
    0,
    0,
    TimeSpan.Zero
  );

  [Fact]
  public async Task TwelveCardsUseOneSummaryReadAndPageStatusWhileUnchangedStatusDoesNotRenderAgain()
  {
    var trucks = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray();
    var loads = trucks
      .Select(truck => new DispatchResponse
      {
        Id = Guid.NewGuid(),
        TruckId = truck,
        Status = "in_transit",
      })
      .ToArray();
    var plans = loads
      .Select(load => Plan(load.TruckId!.Value, load.Id))
      .ToArray();
    var requests = new ConcurrentQueue<Uri>();
    var summaryRequested = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        requests.Enqueue(request.RequestUri!);
        if (request.RequestUri!.AbsolutePath == "/api/dispatch/board/planning")
          summaryRequested.TrySetResult();
        object response = request.RequestUri!.AbsolutePath switch
        {
          "/api/dispatch/board" => new
          {
            items = loads
              .Select(load => new TruckDispatchBoardResponse
              {
                Key = load.TruckId.ToString()!,
                TruckId = load.TruckId,
                TruckNumber = load.TruckId.ToString()!,
                Speed = 40,
                EngineState = "On",
                TrailerNumber = "T",
                Dispatches = [load],
              })
              .ToArray(),
            page = 1,
            pageSize = 12,
            totalCount = 12,
            totalPages = 1,
          },
          "/api/dispatch/board/planning" => plans,
          "/api/dispatch/board/enrichment" =>
            Array.Empty<TruckDispatchEnrichment>(),
          "/api/fleet/hos" => new Dictionary<Guid, TruckHosSnapshot>(),
          "/api/dispatch/board/telemetry" => trucks
            .Select(id => new DispatchTruckStatus(id, 40, "On", "T"))
            .ToArray(),
          _ => throw new InvalidOperationException(
            request.RequestUri.AbsolutePath
          ),
        };
        return Task.FromResult(Ok(response));
      }
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    await summaryRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
    // Cold JSON metadata and twelve card renders share CPU with the full suite;
    // this verifies publication and request counts, not a five-second SLA.
    await component.WaitForAssertionAsync(
      () =>
      {
        var summaries = component.FindComponents<DispatchPlanning>();
        Assert.Equal(12, summaries.Count);
        Assert.All(
          summaries,
          summary =>
          {
            Assert.True(summary.Instance.Managed);
            Assert.NotNull(summary.Instance.Snapshot);
            Assert.False(summary.Instance.Refreshing);
          }
        );
      },
      TimeSpan.FromSeconds(15)
    );
    Assert.Single(
      requests,
      uri => uri.AbsolutePath == "/api/dispatch/board/planning"
    );
    var status = Assert.Single(
      requests,
      uri => uri.AbsolutePath == "/api/dispatch/board/telemetry"
    );
    Assert.All(trucks, id => Assert.Contains($"truckIds={id}", status.Query));
    Assert.DoesNotContain(
      requests,
      uri =>
        uri.AbsolutePath == "/api/fleet/locations"
        || uri.AbsolutePath.EndsWith("/previews")
    );
    Assert.All(
      trucks,
      id =>
        Assert.Null(
          context
            .Services.GetRequiredService<PlanningDisplayCache>()
            .Get($"api/fleet/trucks/{id}/planning")
        )
    );
    await component.InvokeAsync(async () =>
    {
      var renders = component.RenderCount;
      await component.Instance.RefreshBoardAsync(default);
      Assert.Equal(renders, component.RenderCount);
    });
    Assert.Single(
      requests,
      uri => uri.AbsolutePath == "/api/dispatch/board/planning"
    );
    context.Visibility.SetVisible(false);
    var count = requests.Count;
    await component.InvokeAsync(
      () => component.Instance.RefreshBoardAsync(default)
    );
    Assert.Equal(count, requests.Count);
  }

  [Fact]
  public async Task AStopEditInvalidatesOnlyItsTruckAndKeepsAnUnrelatedWarmMapPlan()
  {
    var truck = Guid.NewGuid();
    var other = Guid.NewGuid();
    var load = new DispatchResponse
    {
      Id = Guid.NewGuid(),
      TruckId = truck,
      Status = "in_transit",
    };
    var summaryRequested = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.RequestUri!.AbsolutePath == "/api/dispatch/board/planning")
          summaryRequested.TrySetResult();
        return Task.FromResult(
          Ok(
            request.RequestUri.AbsolutePath switch
            {
              "/api/dispatch/board" => new
              {
                items = new[]
                {
                  new TruckDispatchBoardResponse
                  {
                    Key = truck.ToString(),
                    TruckId = truck,
                    Dispatches = [load],
                  },
                },
                page = 1,
                totalCount = 1,
                totalPages = 1,
              },
              "/api/dispatch/board/planning" => (object)
                new[] { Plan(truck, load.Id) },
              _ => Array.Empty<object>(),
            }
          )
        );
      }
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var cache = context.Services.GetRequiredService<PlanningDisplayCache>();
    var saved = Plan(other, Guid.NewGuid());
    cache.Store($"api/fleet/trucks/{other}/planning", saved);
    cache.Store($"api/fleet/trucks/{truck}/planning", Plan(truck, load.Id));
    var component = context.Render<DispatchList>();
    await summaryRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await component.WaitForAssertionAsync(
      () =>
        Assert.NotNull(
          component.FindComponent<DispatchPlanning>().Instance.Snapshot
        ),
      TimeSpan.FromSeconds(5)
    );
    await component.InvokeAsync(
      () =>
        component
          .FindComponent<DispatchLoadCard>()
          .Instance.StopChanged.InvokeAsync()
    );
    Assert.Null(cache.Get($"api/fleet/trucks/{truck}/planning"));
    Assert.Same(saved, cache.Get($"api/fleet/trucks/{other}/planning"));
  }

  private static AutomaticPlanningResult Plan(Guid truck, Guid dispatch) =>
    new(
      truck,
      dispatch,
      1,
      new(
        new(),
        new()
        {
          Id = dispatch,
          TruckId = truck,
          DispatchId = dispatch,
          Version = 1,
        },
        null,
        null,
        null,
        true
      ),
      null
    );

  private static HttpResponseMessage Ok(object value) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(new { success = true, response = value }),
    };
}
