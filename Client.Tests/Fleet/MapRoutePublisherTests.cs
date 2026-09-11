using Client.Models.DTO.Planning;
using Client.Services;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class MapRoutePublisherTests
{
  [Fact]
  public async Task FuelGaugeInputsPublishWithFullAndMetadataOnlyPayloads()
  {
    var js = new PayloadJs();
    var publisher = new MapRoutePublisher();
    var state = State();
    state.Plan!.Profile.TankGallons = 200;
    state.Plan.FuelPlan = new() { Stops = [new() { Number = 2, ArrivalGallons = 40, BuyGallons = 100,
      DepartureGallons = 140, Unit = "L" }] };
    await publisher.PublishAsync(js, state, false, () => true, default);
    await publisher.PublishAsync(js, state, false, () => true, default);
    foreach (var call in js.Calls)
    {
      using var json = JsonDocument.Parse((byte[])call.Args![0]!);
      Assert.Equal(200, json.RootElement.GetProperty("tankGallons").GetDouble());
      var stop = json.RootElement.GetProperty("fuelPlan").GetProperty("stops")[0];
      Assert.Equal(40, stop.GetProperty("arrivalGallons").GetDouble());
      Assert.Equal(140, stop.GetProperty("departureGallons").GetDouble());
      Assert.Equal("L", stop.GetProperty("unit").GetString());
    }
  }

  [Fact]
  public async Task PollPayloadSizeDoesNotGrowWithUnchangedRoadGeometry()
  {
    var js = new PayloadJs();
    var publisher = new MapRoutePublisher();
    var state = State();
    state.Plan!.Route.Legs = [new(1000, 60000,
      Enumerable.Range(0, 10000).Select(i => new RoutePoint(35 + i * .0001, -80 + i * .0001)).ToList())];
    await publisher.PublishAsync(js, state, false, () => true, default);
    var fullBytes = ((byte[])js.Calls[0].Args![0]!).Length;
    using var full = JsonDocument.Parse((byte[])js.Calls[0].Args![0]!);
    Assert.False(full.RootElement.GetProperty("route").GetProperty("legs")[0].GetProperty("points")[0].TryGetProperty("isValid", out _));
    await publisher.PublishAsync(js, state, false, () => true, default);
    var updateBytes = ((byte[])js.Calls[1].Args![0]!).Length;
    Assert.True(fullBytes > 400000, $"Fixture must exercise a substantial geometry payload; got {fullBytes} bytes.");
    Assert.True(updateBytes < 1024, $"Unchanged geometry was retransmitted: {updateBytes} bytes.");
    Assert.True(updateBytes * 100 < fullBytes);
  }

  [Fact]
  public async Task LatestSelectionIsTheFinalPublishedPayload()
  {
    var js = new PayloadJs();
    var publisher = new MapRoutePublisher();
    var acknowledgement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    js.PendingAcknowledgement = acknowledgement.Task;
    var first = publisher.PublishAsync(js, State(), false, () => true, default);
    var selectedId = Guid.NewGuid();
    var selected = State(truckId: selectedId);
    try
    {
      await js.DeferredCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
      await publisher.PublishAsync(js, selected, true, () => true, default);
      Assert.Equal(2, js.Calls.Count);
    }
    finally { acknowledgement.TrySetResult(true); }
    await first;
    var last = js.Calls[^1];
    Assert.Equal("setRouteBytes", last.Name);
    Assert.Equal(true, last.Args![2]);
    using var json = JsonDocument.Parse((byte[])last.Args![0]!);
    Assert.Equal(selectedId, json.RootElement.GetProperty("truckId").GetGuid());
    Assert.True(json.RootElement.GetProperty("route").TryGetProperty("legs", out _));
    Assert.False(json.RootElement.GetProperty("route").TryGetProperty("points", out _));
    // An old acknowledgement must not overwrite the new selection's geometry identity.
    await publisher.PublishAsync(js, selected, false, () => true, default);
    Assert.Equal(3, js.Calls.Count);
    using var refresh = JsonDocument.Parse((byte[])js.Calls[^1].Args![0]!);
    Assert.Equal(selectedId, refresh.RootElement.GetProperty("truckId").GetGuid());
    Assert.True(refresh.RootElement.GetProperty("geometryOmitted").GetBoolean());
    Assert.False(refresh.RootElement.TryGetProperty("route", out _));
  }

  [Fact]
  public async Task SelectionOrDisposalGuardPreventsPublishing()
  {
    var js = new PayloadJs();
    await new MapRoutePublisher().PublishAsync(js, State(), false, () => false, default);
    Assert.Empty(js.Calls);
  }

  [Fact]
  public async Task LifetimeCancellationPreventsPublishing()
  {
    var js = new PayloadJs();
    using var lifetime = new CancellationTokenSource();
    lifetime.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
      new MapRoutePublisher().PublishAsync(js, State(), false, () => true, lifetime.Token));
    Assert.Empty(js.Calls);
  }

  [Fact]
  public async Task AcknowledgedGeometryIsOmittedWhileFreshStopMetadataStillPublishes()
  {
    var js = new PayloadJs();
    var publisher = new MapRoutePublisher();
    var state = State();
    await publisher.PublishAsync(js, state, false, () => true, default);
    state.Plan!.Stops = [new(Guid.NewGuid(), "Updated stop", "Address", 1, new(40, -80))];
    await publisher.PublishAsync(js, state, false, () => true, default);
    using var json = JsonDocument.Parse((byte[])js.Calls[^1].Args![0]!);
    Assert.True(json.RootElement.GetProperty("geometryOmitted").GetBoolean());
    Assert.False(json.RootElement.TryGetProperty("route", out _));
    Assert.Equal("Updated stop", json.RootElement.GetProperty("stops")[0].GetProperty("name").GetString());
  }

  [Fact]
  public async Task ExactDispatchIdentityPublishesInFullAndMetadataPayloads()
  {
    var js = new PayloadJs();
    var publisher = new MapRoutePublisher();
    var state = State();
    var dispatchId = Guid.NewGuid();
    state.Plan!.DispatchId = dispatchId;
    await publisher.PublishAsync(js, state, false, () => true, default);
    await publisher.PublishAsync(js, state, false, () => true, default);
    Assert.Equal(2, js.Calls.Count);
    for (var index = 0; index < js.Calls.Count; index++)
    {
      using var payload = JsonDocument.Parse((byte[])js.Calls[index].Args![0]!);
      Assert.Equal(dispatchId, payload.RootElement.GetProperty("dispatchId").GetGuid());
      Assert.NotEqual(payload.RootElement.GetProperty("id").GetGuid(), payload.RootElement.GetProperty("dispatchId").GetGuid());
      Assert.Equal(index > 0, payload.RootElement.GetProperty("geometryOmitted").GetBoolean());
    }
    state.Plan.DispatchId = Guid.Empty;
    await publisher.PublishAsync(js, state, false, () => true, default);
    using var unknown = JsonDocument.Parse((byte[])js.Calls[^1].Args![0]!);
    Assert.Equal(Guid.Empty, unknown.RootElement.GetProperty("dispatchId").GetGuid());
  }

  [Theory]
  [InlineData("target")]
  [InlineData("plan")]
  [InlineData("version")]
  [InlineData("truck")]
  public async Task DifferentGeometryIdentityOrTargetRequiresFullPayload(string change)
  {
    var js = new PayloadJs();
    var publisher = new MapRoutePublisher();
    var state = State();
    await publisher.PublishAsync(js, state, false, () => true, default);
    if (change == "target") js = new();
    if (change == "plan") state.Plan!.Id = Guid.NewGuid();
    if (change == "version") state.Plan!.Version++;
    if (change == "truck") state.Plan!.TruckId = Guid.NewGuid();
    await publisher.PublishAsync(js, state, false, () => true, default);
    using var json = JsonDocument.Parse((byte[])js.Calls[^1].Args![0]!);
    Assert.False(json.RootElement.GetProperty("geometryOmitted").GetBoolean());
    Assert.True(json.RootElement.TryGetProperty("route", out _));
  }

  [Fact]
  public async Task RendererLosingGeometryReceivesAFullRetry()
  {
    var js = new PayloadJs();
    var publisher = new MapRoutePublisher();
    var state = State();
    await publisher.PublishAsync(js, state, false, () => true, default);
    js.RejectNext = true;
    await publisher.PublishAsync(js, state, false, () => true, default);
    Assert.Equal(3, js.Calls.Count);
    using var omitted = JsonDocument.Parse((byte[])js.Calls[1].Args![0]!);
    using var full = JsonDocument.Parse((byte[])js.Calls[2].Args![0]!);
    Assert.True(omitted.RootElement.GetProperty("geometryOmitted").GetBoolean());
    Assert.False(full.RootElement.GetProperty("geometryOmitted").GetBoolean());
    Assert.True(full.RootElement.TryGetProperty("route", out _));
  }

  private static RoutePlanningState State(Guid? truckId = null) =>
    new(new(), new RoutePlan { Id = Guid.NewGuid(), Version = 1, TruckId = truckId ?? Guid.NewGuid() }, null, null, null, true);

  private sealed class PayloadJs : IJSObjectReference
  {
    public bool RejectNext;
    public Task<bool>? PendingAcknowledgement;
    public TaskCompletionSource DeferredCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<(string Name, object?[]? Args)> Calls { get; } = [];
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
      Calls.Add((identifier, args));
      if (PendingAcknowledgement is { } pending)
      {
        PendingAcknowledgement = null;
        DeferredCallStarted.TrySetResult();
        return new(AcknowledgeAsync<TValue>(pending));
      }
      var applied = !RejectNext;
      RejectNext = false;
      return ValueTask.FromResult((TValue)(object)applied);
    }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
      InvokeAsync<TValue>(identifier, args);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    private static async Task<TValue> AcknowledgeAsync<TValue>(Task<bool> acknowledgement) =>
      (TValue)(object)await acknowledgement;
  }
}
