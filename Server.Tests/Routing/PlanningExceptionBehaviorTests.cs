using Application.Behaviors;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Interfaces;
using Application.Models;
using Domain.Models.Routing;
using Domain.Rules;
using MediatR;

namespace Server.Tests.Routing;

// How a planning request answers what planning refuses: another pass
// holding the truck's inputs is a conflict to retry (409), any other
// refusal a plain failure (400). A request that is not a planning request
// is not answered here: its unexpected failure reaches the HTTP boundary.
[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class PlanningExceptionBehaviorTests
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task AutomaticFuelRefreshPreservesTheSchedulingRefusal(bool busy)
  {
    var retry = new DateTime(2026, 9, 27, 14, 5, 55, DateTimeKind.Utc);
    var refusal = busy
      ? RoutePlanningException.InputsBusy(retry)
      : new RoutePlanningException("Route service unavailable.", retry);
    var request = new RecalculateFuelPlanCommand(Guid.NewGuid())
    {
      AutomaticRefreshRevision = retry.AddDays(-1),
    };
    var behavior =
      new PlanningExceptionBehavior<
        RecalculateFuelPlanCommand,
        AutomaticPlanningResult
      >();

    var actual = await Assert.ThrowsAsync<RoutePlanningException>(
      () => behavior.Handle(request, _ => throw refusal, default)
    );

    Assert.Same(refusal, actual);
    Assert.Equal(retry, actual.RetryAfter);
    Assert.Equal(busy, actual.Busy);
  }

  [Fact]
  public async Task InteractiveFuelRefreshStillReturnsAConflict()
  {
    var behavior =
      new PlanningExceptionBehavior<
        RecalculateFuelPlanCommand,
        AutomaticPlanningResult
      >();
    var answer = await behavior.Handle(
      new RecalculateFuelPlanCommand(Guid.NewGuid()),
      _ =>
        throw RoutePlanningException.InputsBusy(DateTime.UtcNow.AddSeconds(5)),
      default
    );

    Assert.False(answer.Success);
    Assert.Equal(409, answer.StatusCode);
  }

  [Theory]
  [InlineData(true, 409)]
  [InlineData(false, 400)]
  public async Task APlanningRefusalIsAnsweredWithItsStatus(
    bool busy,
    int status
  )
  {
    var refusal = busy
      ? RoutePlanningException.InputsBusy(DateTime.UtcNow.AddSeconds(5))
      : new RoutePlanningException(
        "Route service unavailable.",
        DateTime.UtcNow.AddMinutes(1)
      );

    var answer = await new PlanningExceptionBehavior<Planning, bool>().Handle(
      new Planning(),
      _ => throw refusal,
      default
    );

    Assert.Equal(status, answer.StatusCode);
    Assert.Equal(refusal.Message, Assert.Single(answer.Errors!));
  }

  [Fact]
  public async Task AnotherRequestIsNotAnsweredHere()
  {
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        new PlanningExceptionBehavior<Other, bool>().Handle(
          new Other(),
          _ =>
            throw RoutePlanningException.InputsBusy(
              DateTime.UtcNow.AddSeconds(5)
            ),
          default
        )
    );
  }

  private sealed record Planning
    : IRequest<RequestResponse<bool>>,
      IPlanningRequest;

  private sealed record Other : IRequest<RequestResponse<bool>>;
}
