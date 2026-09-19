using System.Text.Json;
using API.Controllers;
using Application.Features.Routing.Models;
using Application.Features.Routing.Queries;
using Application.Models;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class PlanningDigestTests
{
  private static readonly Guid Truck = Guid.NewGuid();

  [Fact]
  public async Task PlanningReadsAreTaggedWithABodyDigestAndAnsweredWithoutABodyWhenUnchanged()
  {
    var result = RequestResponse<AutomaticPlanningResult>.Ok(new(Truck, Guid.NewGuid(), 7, null, "No remaining stops."));
    var (first, firstContext) = await ReadAsync(result, null);
    var body = Assert.IsType<FileContentResult>(first);
    Assert.Equal("application/json; charset=utf-8", body.ContentType);
    var tag = firstContext.Response.Headers.ETag.ToString();
    Assert.StartsWith("W/\"", tag);
    Assert.Equal("private, no-cache", firstContext.Response.Headers.CacheControl.ToString());
    var wire = JsonSerializer.Deserialize<JsonElement>(body.FileContents);
    Assert.True(wire.GetProperty("success").GetBoolean());
    Assert.Equal(Truck, wire.GetProperty("response").GetProperty("truckId").GetGuid());

    var (second, secondContext) = await ReadAsync(result, tag);
    Assert.Equal(304, Assert.IsType<StatusCodeResult>(second).StatusCode);
    Assert.Equal(tag, secondContext.Response.Headers.ETag.ToString());

    var changed = RequestResponse<AutomaticPlanningResult>.Ok(result.Response! with { Message = "Recalculating." });
    var (third, thirdContext) = await ReadAsync(changed, tag);
    Assert.IsType<FileContentResult>(third);
    Assert.NotEqual(tag, thirdContext.Response.Headers.ETag.ToString());
  }

  [Fact]
  public async Task FailedPlanningReadsKeepTheirStatusAndAreNotTagged()
  {
    var (result, context) = await ReadAsync(RequestResponse<AutomaticPlanningResult>.Fail("Unavailable.", 503), "*");
    Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
    Assert.True(string.IsNullOrEmpty(context.Response.Headers.ETag));
  }

  private static async Task<(IActionResult Result, DefaultHttpContext Context)> ReadAsync(
    RequestResponse<AutomaticPlanningResult> result, string? ifNoneMatch)
  {
    var context = new DefaultHttpContext
    {
      RequestServices = new ServiceCollection().AddSingleton<IMediator>(new StubMediator(result))
        .AddSingleton(Options.Create(new JsonOptions())).BuildServiceProvider()
    };
    if (ifNoneMatch is not null) context.Request.Headers.IfNoneMatch = ifNoneMatch;
    var controller = new RoutePlanningController { ControllerContext = new() { HttpContext = context } };
    var response = await controller.Truck(Truck, default);
    Assert.NotNull((response as IStatusCodeActionResult)?.StatusCode ?? (response is FileContentResult ? 200 : null));
    return (response, context);
  }

  private sealed class StubMediator(RequestResponse<AutomaticPlanningResult> result) : IMediator
  {
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
      Assert.IsType<GetTruckPlanningQuery>(request);
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
