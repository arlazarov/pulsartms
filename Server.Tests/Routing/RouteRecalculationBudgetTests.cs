using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Policies;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RouteRecalculationBudgetTests
{
  private static readonly DateTime Now = new(
    2026,
    9,
    7,
    18,
    0,
    0,
    DateTimeKind.Utc
  );

  private static RouteRecalculationAttempt Attempt(double minutes) =>
    new()
    {
      CreatedAt = Now.AddMinutes(-minutes),
      Latitude = 40,
      Longitude = -80,
    };

  [Fact]
  public void UnconfiguredBudgetRemainsEnabledByDefault() =>
    Assert.True(new RouteRecalculationBudgetOptions().Enabled);

  [Fact]
  public void CooldownAndMovementBothRequired()
  {
    Assert.Null(RouteRecalculationBudget.BlockedUntil([], new(41, -80), Now));
    Assert.NotNull(
      RouteRecalculationBudget.BlockedUntil([Attempt(5)], new(41, -80), Now)
    );
    Assert.NotNull(
      RouteRecalculationBudget.BlockedUntil([Attempt(16)], new(40, -80), Now)
    );
    Assert.Null(
      RouteRecalculationBudget.BlockedUntil([Attempt(16)], new(41, -80), Now)
    );
  }

  [Fact]
  public void RollingBudgetsIncludeEveryAttempt()
  {
    Assert.Equal(
      Now.AddMinutes(10),
      RouteRecalculationBudget.BlockedUntil(
        [Attempt(20), Attempt(35), Attempt(50)],
        new(41, -80),
        Now
      )
    );
    var attempts = Enumerable
      .Range(1, 12)
      .Select(x => Attempt(x * 60))
      .ToArray();
    Assert.Equal(
      Now.AddHours(12),
      RouteRecalculationBudget.BlockedUntil(attempts, new(41, -80), Now)
    );
  }
}
