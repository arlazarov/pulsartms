using System.Net;
using System.Net.Http.Json;
using Client.Models.DTO;
using Client.Models.DTO.Planning;
using Client.Pages.FleetMap;
using Client.Services;
using Client.Tests.Support;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class PlanningExecutionIdentityTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task PendingSummaryRetainsOnlyTheSameAssignment(bool changed)
  {
    var previous = Result(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    var pending = previous with
    {
      State = null,
      IsRefreshing = true,
      AssignmentRevision = changed ? 2 : 1,
    };
    using var client = new HttpClient(
      new StubHttpMessageHandler(
        (_, _) =>
          Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
              Content = JsonContent.Create(
                new RequestResponseDTO<AutomaticPlanningResult>
                {
                  Success = true,
                  Response = pending,
                }
              ),
            }
          )
      )
    )
    {
      BaseAddress = new("http://localhost/"),
    };
    using var cache = new PlanningDisplayCache(new ApiService(client));
    var url = TruckUrl(previous.TruckId);
    cache.Store(url, previous);
    var response = await cache.RefreshAsync(url, default);
    Assert.True(response.Response!.IsRefreshing);
    if (changed)
      Assert.Null(response.Response.State);
    else
      Assert.Equal(previous.State!.Plan!.Id, response.Response.State!.Plan!.Id);
  }

  [Fact]
  public void ScopedReadsCannotPopulateTheCommercialDispatchAlias()
  {
    using var client = new HttpClient();
    using var cache = new PlanningDisplayCache(new ApiService(client));
    var result = Result(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    var alias = $"api/dispatch/{result.DispatchId}/planning/automatic";
    cache.Store(alias, result);
    Assert.Null(cache.Get(alias));

    cache.Store(alias, Result(result.TruckId, result.DispatchId!.Value, null));
    Assert.NotNull(cache.Get(alias));
    cache.Store(TruckUrl(result.TruckId), result);
    Assert.Null(cache.Get(alias));
    Assert.Same(result, cache.Get(TruckUrl(result.TruckId)));
  }

  [Fact]
  public void RecalculationDoesNotReplaceAnotherTrucksLegOfTheSameLoad()
  {
    using var client = new HttpClient();
    using var cache = new PlanningDisplayCache(new ApiService(client));
    var first = Result(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    var second = Result(
      Guid.NewGuid(),
      first.DispatchId!.Value,
      Guid.NewGuid()
    );
    cache.Store(TruckUrl(first.TruckId), first);
    cache.Store(TruckUrl(second.TruckId), second);
    var refreshed = first with { Message = "Recalculated" };
    cache.StoreRecalculated(refreshed);
    Assert.Same(refreshed, cache.Get(TruckUrl(first.TruckId)));
    Assert.Same(second, cache.Get(TruckUrl(second.TruckId)));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ChangedExecutionCannotReuseOmittedGeometry(bool changeLeg)
  {
    var first = Result(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    var next = Result(
      first.TruckId,
      first.DispatchId!.Value,
      changeLeg ? Guid.NewGuid() : first.ExecutionLegId
    );
    next.State!.Plan!.Id = first.State!.Plan!.Id;
    next.State.Plan.Version = first.State.Plan.Version;
    if (!changeLeg)
      next.State.Plan.AssignmentRevision++;
    var requests = 0;
    using var client = new HttpClient(
      new StubHttpMessageHandler(
        (_, _) =>
        {
          requests++;
          next.State.Plan.GeometryOmitted = requests == 1;
          return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
              Content = JsonContent.Create(
                new RequestResponseDTO<AutomaticPlanningResult>
                {
                  Success = true,
                  Response = next,
                }
              ),
            }
          );
        }
      )
    )
    {
      BaseAddress = new("http://localhost/"),
    };
    using var cache = new PlanningDisplayCache(new ApiService(client));
    var url = TruckUrl(first.TruckId);
    cache.Store(url, first);
    var response = await cache.RefreshAsync(url, default);
    Assert.True(response.Success);
    Assert.Equal(2, requests);
    Assert.False(response.Response!.State!.Plan!.GeometryOmitted);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void DisplayGraceDoesNotMatchDifferentExecution(bool changeLeg)
  {
    var first = Result(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    var next = Result(
      first.TruckId,
      first.DispatchId!.Value,
      changeLeg ? Guid.NewGuid() : first.ExecutionLegId
    );
    if (!changeLeg)
      next.State!.Plan!.AssignmentRevision++;
    var memory = new FleetRouteDisplayMemory();
    memory.Update(first.State, DateTime.UtcNow);
    Assert.False(memory.Matches(next.State));
  }

  private static string TruckUrl(Guid id) => $"api/fleet/trucks/{id}/planning";

  private static AutomaticPlanningResult Result(
    Guid truckId,
    Guid dispatchId,
    Guid? executionLegId
  ) =>
    new(
      truckId,
      dispatchId,
      123,
      new(
        new(),
        new()
        {
          Id = Guid.NewGuid(),
          TruckId = truckId,
          DispatchId = dispatchId,
          ExecutionLegId = executionLegId,
          AssignmentRevision = 1,
          Version = 1,
        },
        null,
        null,
        null,
        false
      ),
      null
    )
    {
      ExecutionLegId = executionLegId,
      AssignmentRevision = 1,
    };
}
