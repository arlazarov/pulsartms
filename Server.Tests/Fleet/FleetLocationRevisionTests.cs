using API;
using API.Controllers;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Models;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Server.Tests.Support;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class FleetLocationRevisionTests
{
  [Fact]
  public void EveryPublishedSnapshotGetsADistinctRevision()
  {
    var telemetry = new ServerTelemetry();
    var first = new FleetLocationsResponse();
    var second = new FleetLocationsResponse();
    telemetry.Set(first);
    telemetry.Set(second);
    Assert.Same(second, telemetry.Current);
    Assert.False(string.IsNullOrWhiteSpace(first.Revision));
    Assert.NotEqual(first.Revision, second.Revision);
    Assert.NotEqual(first.Revision, new ServerTelemetry().Also(x => x.Set(new())).Current!.Revision);
  }

  [Fact]
  public async Task PolledTelemetryKeepsOneRevisionWhileCached()
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new FleetTelemetryCache(memory);
    var loads = 0;
    Task<FleetLocationsResponse> Load(CancellationToken _) { loads++; return Task.FromResult(new FleetLocationsResponse()); }
    var first = await cache.GetAsync(Load, default);
    var second = await cache.GetAsync(Load, default);
    Assert.Equal(1, loads);
    Assert.False(string.IsNullOrWhiteSpace(first.Revision));
    Assert.Equal(first.Revision, second.Revision);
  }

  [Fact]
  public async Task BoardReadsShareTheSnapshotRevisionWithoutLocationHistory()
  {
    var telemetry = new ServerTelemetry();
    var snapshot = new FleetLocationsResponse
    {
      Trucks = [new() { TruckId = Guid.NewGuid() }],
      Points = [new() { TruckId = Guid.NewGuid() }, new() { TruckId = Guid.NewGuid() }]
    };
    telemetry.Set(snapshot);
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var stream = new FleetLocationStream(TimeProvider.System);
    var handler = new GetFleetLocationsHandler(null!, null!, null!, new FleetTelemetryCache(memory), stream, telemetry,
      Microsoft.Extensions.Options.Options.Create(new Application.Features.Synchronization.Options.SynchronizationOptions { Enabled = true }));
    var full = (await handler.Handle(new(), default)).Response!;
    var board = (await handler.Handle(new(IncludePoints: false), default)).Response!;
    Assert.Same(snapshot, full);
    Assert.Empty(board.Points);
    Assert.Same(snapshot.Trucks, board.Trucks);
    Assert.Equal(snapshot.Revision, board.Revision);
    Assert.Equal(2, snapshot.Points.Count);
  }

  [Theory]
  [InlineData("W/\"abc-1\"", true)]
  [InlineData("\"abc-1\"", true)]
  [InlineData("\"other\", W/\"abc-1\"", true)]
  [InlineData("*", true)]
  [InlineData("W/\"abc-10\"", false)]
  [InlineData("abc-1", false)]
  [InlineData("", false)]
  public void IfNoneMatchUsesWeakComparison(string header, bool expected)
  {
    Assert.Equal(expected, EntityTags.Matches(header, "abc-1"));
    Assert.Equal("W/\"abc-1\"", EntityTags.Weak("abc-1"));
  }

  [Theory]
  [InlineData(null, 200)]
  [InlineData("W/\"rev-7\"", 304)]
  [InlineData("\"rev-6\"", 200)]
  public async Task LocationsAnswerUnchangedRevalidationWithoutABody(string? ifNoneMatch, int expected)
  {
    var mediator = new StubMediator(RequestResponse<FleetLocationsResponse>.Ok(new() { Revision = "rev-7" }));
    var context = new DefaultHttpContext
    {
      RequestServices = new ServiceCollection().AddSingleton<IMediator>(mediator).BuildServiceProvider()
    };
    if (ifNoneMatch is not null) context.Request.Headers.IfNoneMatch = ifNoneMatch;
    var controller = new FleetController { ControllerContext = new() { HttpContext = context } };
    var result = await controller.GetLocations(default);
    Assert.Equal(expected, (result as IStatusCodeActionResult)?.StatusCode);
    Assert.Equal("W/\"rev-7\"", context.Response.Headers.ETag.ToString());
    Assert.Equal("private, no-cache", context.Response.Headers.CacheControl.ToString());
    if (expected == 304) Assert.IsType<StatusCodeResult>(result);
    else Assert.Same(mediator.Result, Assert.IsType<ObjectResult>(result).Value);
  }

  [Fact]
  public async Task FailedReadsAreNotRevisioned()
  {
    var mediator = new StubMediator(RequestResponse<FleetLocationsResponse>.Fail("Unavailable.", 503));
    var context = new DefaultHttpContext
    {
      RequestServices = new ServiceCollection().AddSingleton<IMediator>(mediator).BuildServiceProvider()
    };
    context.Request.Headers.IfNoneMatch = "*";
    var controller = new FleetController { ControllerContext = new() { HttpContext = context } };
    var result = await controller.GetLocations(default);
    Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
    Assert.True(string.IsNullOrEmpty(context.Response.Headers.ETag));
  }

  private sealed class StubMediator(RequestResponse<FleetLocationsResponse> result) : IMediator
  {
    public RequestResponse<FleetLocationsResponse> Result => result;
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
      Assert.IsType<GetFleetLocationsQuery>(request);
      return Task.FromResult((TResponse)(object)result);
    }
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
    public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task Publish(object notification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => throw new NotSupportedException();
  }
}

file static class Fluent
{
  public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}
