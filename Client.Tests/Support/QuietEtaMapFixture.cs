using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Bunit;
using Client.Models.DTO.Planning;
using Client.Pages.FleetMap;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;

namespace Client.Tests.Support;

internal sealed class QuietEtaMapFixture : IDisposable
{
  private readonly ClientComponentContext context;
  private readonly CancellationTokenSource shutdown = new();
  private readonly Channel<TaskCompletionSource<HttpResponseMessage>> pending = Channel.CreateUnbounded<TaskCompletionSource<HttpResponseMessage>>();
  public Guid TruckId { get; } = Guid.NewGuid();
  public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
  public MapInteropStub Js { get; } = new();
  public AutomaticPlanningResult Planning { get; set; }
  public NextLoadRoute Future { get; }
  public bool HoldPlanning { get; set; }

  public QuietEtaMapFixture()
  {
    var dispatchId = Guid.NewGuid();
    var current = new PlanStop(Guid.NewGuid(), "Current delivery", "123 Main Street", 1, new(41, -79))
      { Job = "Drop Off", ScheduledDate = new(2026, 9, 10), ScheduledTime = new(14, 0) };
    Future = new(Guid.NewGuid(), 202, "ready", [new(10, 600, [new(41, -79), new(42, -78)])],
      [new(41, -79, "Future pickup") { Id = Guid.NewGuid() }, new(42, -78, "Future delivery") { Id = Guid.NewGuid() }])
      { Deadhead = new(15, []) };
    var now = Clock.GetUtcNow().UtcDateTime;
    var forecast = new DispatchEta(now, now.AddMinutes(2),
      [new(current.Id, now.AddHours(1), "UTC", now.AddHours(2), 0, 60, 0) { DispatchId = dispatchId },
        new(Future.Stops[1].Id, now.AddHours(2), "UTC", null, 0, 120, 0) { DispatchId = Future.Id }], null, []);
    Planning = new(TruckId, dispatchId, 1358, new(new(),
      new() { Id = Guid.NewGuid(), TruckId = TruckId, DispatchId = dispatchId, Version = 1, Stops = [current],
        OriginalPlannedMiles = 100, Route = new() { Miles = 100, Seconds = 6000,
          Legs = [new(100, 6000, [new(40, -80), current.Point])] },
        Tracking = new() { NextStopId = current.Id } },
      new(10, 90, 5400, 0, false, false, now, new(40.1, -79.9)), null, null, true) { Eta = forecast }, null);
    context = new(RespondAsync);
    context.Services.AddSingleton<TimeProvider>(Clock);
    context.Services.AddSingleton<IJSRuntime>(Js);
    context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    context.Services.GetRequiredService<PlanningDisplayCache>().Store($"api/fleet/trucks/{TruckId}/planning", Planning);
    Js.Respond = (name, _) => Task.FromResult<object?>(name is "import" or "createFleetMap" ? Js
      : name is "setRouteBytes" or "focusTruck" ? true : null);
  }

  public async Task<IRenderedComponent<FleetMap>> SelectAsync()
  {
    var component = context.Render<FleetMap>();
    component.WaitForAssertion(() => Assert.Contains(Js.Calls, call => call.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(TruckId.ToString()));
    await component.InvokeAsync(() => component.Instance.OnRouteProgress(TruckId.ToString(), 90, 10));
    await component.FindAll("label").Single(label => label.TextContent.Trim() == "Next loads")
      .QuerySelector("input")!.ChangeAsync(new ChangeEventArgs { Value = true });
    await component.InvokeAsync(() => component.Instance.OnNextLoadSelected(TruckId.ToString(),
      Planning.DispatchId!.Value.ToString(), Future.Id.ToString(), 1));
    return component;
  }

  public Task<TaskCompletionSource<HttpResponseMessage>> ReadPendingAsync() =>
    pending.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

  public static void Reply(TaskCompletionSource<HttpResponseMessage> request, AutomaticPlanningResult response) =>
    request.SetResult(Ok(response));

  private Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
  {
    var path = request.RequestUri!.AbsolutePath;
    if (path == "/api/fleet/locations") return Task.FromResult(Ok(new
      { trucks = new[] { new { truckId = TruckId, unitNumber = "54777" } }, points = Array.Empty<object>() }));
    if (path.EndsWith("/planning", StringComparison.Ordinal))
    {
      if (!HoldPlanning) return Task.FromResult(Ok(Planning));
      var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
      pending.Writer.TryWrite(response);
      return response.Task.WaitAsync(shutdown.Token);
    }
    if (path.EndsWith("/planning/preview", StringComparison.Ordinal)) return Task.FromResult(Ok(Planning));
    if (path.EndsWith("/next-routes", StringComparison.Ordinal))
      return Task.FromResult(Ok(new NextLoadRoutesResponse("future", request.RequestUri!.Query.Contains("revision=future", StringComparison.Ordinal), [Future])));
    if (path == $"/api/dispatch/{Future.Id}") return Task.FromResult(Ok(new
      { id = Future.Id, loadNumber = Future.LoadNumber }));
    if (path == $"/api/dispatch/{Planning.DispatchId}") return Task.FromResult(Ok(new
      { id = Planning.DispatchId, loadNumber = Planning.LoadNumber }));
    return Task.FromResult(Ok(Array.Empty<object>()));
  }

  private static HttpResponseMessage Ok(object response) => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(new { success = true, response }) };

  public void Dispose()
  {
    shutdown.Cancel();
    context.Dispose();
    shutdown.Dispose();
  }
}
