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

  // An open map acknowledged a plan; the server then gives the plan its
  // base road as reference, or another base road's, at the same version.
  // The metadata answer names where the reference came from but omits its
  // points, so a reference from elsewhere than the one held is read in full
  // once; the one held is reused. Legs and miles are the same in every case:
  // only the source says which reference it is. The version the fuel plan
  // and ETA follow does not move.
  [Theory]
  [InlineData(null, 2)]
  [InlineData("road-a", 1)]
  [InlineData("road-b", 2)]
  public async Task AReferenceGivenAtTheSameVersionReachesAnOpenMap(
    string? heldSource,
    int reads
  )
  {
    var first = Result(Guid.NewGuid(), Guid.NewGuid(), null);
    var given = new List<RoutePoint> { new(40, -80), new(40.5, -79.5) };
    var drawn = new List<RoutePoint> { new(41, -81), new(41.2, -80.9) };
    TruckRoute Reference(List<RoutePoint> points) =>
      new() { Miles = 110, Legs = [new(110, 7200, [.. points])] };
    if (heldSource is not null)
    {
      first.State!.Plan!.ReferenceRoute = Reference(drawn);
      first.State.Plan.ReferenceSource = heldSource;
    }
    var urls = new List<string>();
    using var client = new HttpClient(
      new StubHttpMessageHandler(
        (request, _) =>
        {
          urls.Add(request.RequestUri!.PathAndQuery);
          var omitted = request.RequestUri.Query.Contains("knownVersion");
          var next = Result(first.TruckId, first.DispatchId!.Value, null);
          next.State!.Plan!.Id = first.State!.Plan!.Id;
          next.State.Plan.Version = first.State.Plan.Version;
          next.State.Plan.GeometryOmitted = omitted;
          next.State.Plan.ReferenceRoute = Reference(omitted ? [] : given);
          next.State.Plan.ReferenceSource = "road-a";
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

    Assert.Equal(reads, urls.Count);
    Assert.Contains("knownVersion", urls[0]);
    if (reads == 2)
      Assert.DoesNotContain("knownVersion", urls[1]);
    var plan = response.Response!.State!.Plan!;
    Assert.False(plan.GeometryOmitted);
    Assert.Equal(first.State!.Plan!.Version, plan.Version);
    Assert.Equal("road-a", plan.ReferenceSource);
    Assert.Equal(
      reads == 2 ? given : drawn,
      Assert.Single(plan.ReferenceRoute!.Legs).Points
    );
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
