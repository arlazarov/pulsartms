using Application.Behaviors;
using Application.Features.Routing.Interfaces;
using Application.Models;
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
