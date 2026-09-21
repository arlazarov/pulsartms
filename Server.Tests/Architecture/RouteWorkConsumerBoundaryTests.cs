using System.Collections.Immutable;
using System.Reflection;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Eta.Services;
using Application.Features.Execution.Queries;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class RouteWorkConsumerBoundaryTests
{
  [Fact]
  public void NativeReadAndEtaContractsExposeCapturedWork()
  {
    Assert.Equal(
      typeof(IReadOnlyList<ExecutionLoadSnapshot>),
      typeof(TruckExecutionLoads).GetProperty("Loads")!.PropertyType
    );
    Assert.Equal(
      typeof(RouteWorkSnapshot),
      typeof(ExecutionLoadSnapshot).GetProperty("Work")!.PropertyType
    );
    Assert.Equal(
      typeof(ImmutableArray<RouteWorkSnapshot>),
      typeof(EtaChainDescription).GetProperty("Loads")!.PropertyType
    );
  }

  [Fact]
  public void RoadsAndConnectionsConsumeImmutableWork()
  {
    var preparation = typeof(BaseRouteService).GetMethod(
      "EnsureCoreAsync",
      BindingFlags.Instance | BindingFlags.NonPublic
    )!;
    Assert.Equal(
      typeof(RouteWorkSnapshot),
      preparation.GetParameters()[0].ParameterType
    );
    Assert.Equal(
      typeof(RouteWorkSnapshot),
      typeof(DeadheadConnection).GetProperty("Current")!.PropertyType
    );
    Assert.Equal(
      typeof(RouteWorkStop),
      typeof(DeadheadConnection).GetProperty("From")!.PropertyType
    );
    Assert.Equal(
      typeof(ImmutableArray<RouteWorkStop>),
      typeof(RouteWorkSnapshot).GetProperty("Stops")!.PropertyType
    );
    Assert.DoesNotContain(
      typeof(RouteWorkProjection).GetMethods(),
      x => x.Name == "ToLoad"
    );
  }

  [Fact]
  public void HistoryContractDoesNotExposeMutableSourceEntities()
  {
    var read = typeof(IDeadheadHistoryReader).GetMethod("ReadLoadedAsync")!;
    Assert.Equal(
      typeof(IReadOnlyCollection<RouteWorkSnapshot>),
      read.GetParameters()[0].ParameterType
    );
    Assert.Equal(
      typeof(RouteWorkSnapshot),
      typeof(DeadheadHistorySource).GetProperty("Current")!.PropertyType
    );
    Assert.Equal(
      typeof(ImmutableArray<RouteWorkSnapshot>),
      typeof(DeadheadHistorySource).GetProperty("Predecessors")!.PropertyType
    );
  }

  [Fact]
  public void FinancialPersistenceUsesOnlyItsExplicitInputContract()
  {
    var save = typeof(DispatchRates).GetMethod("SaveAsync")!;
    Assert.Equal(
      typeof(DispatchRateInputs),
      save.GetParameters()[0].ParameterType
    );
  }
}
