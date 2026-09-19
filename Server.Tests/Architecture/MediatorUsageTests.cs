using MediatR;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class MediatorUsageTests
{
  // Remaining in-service MediatR dispatch is debt: this list may only shrink. Services call
  // other services directly so internal reads do not re-enter the request pipeline.
  private static readonly string[] Debt = ["FuelPlanningService", "GmailWatchLifecycle", "RoutePlanningService", "TruckFuelPlans"];

  [Fact]
  public void ApplicationServicesDoNotDispatchThroughMediatRBeyondListedDebt()
  {
    var services = typeof(Application.DependencyInjection).Assembly.GetTypes()
      .Where(type => type.IsClass && !type.IsAbstract && type.Namespace?.Split('.').Contains("Services") == true)
      .Where(type => type.GetConstructors().SelectMany(constructor => constructor.GetParameters())
        .Any(parameter => parameter.ParameterType == typeof(ISender) || parameter.ParameterType == typeof(IMediator)))
      .Select(type => type.Name).Order().ToArray();
    Assert.Equal(Debt, services);
  }
}
