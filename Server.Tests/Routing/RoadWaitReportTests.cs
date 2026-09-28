using Application.Features.Routing.Background;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Server.Tests.Routing;

// Road preparation says why a load waits (audit D1): once per reason and
// inputs, as a code, for each carrier's load, within a bound; the retry
// itself is not its business.
[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RoadWaitReportTests
{
  private static readonly Guid Carrier = Guid.NewGuid();

  [Fact]
  public void TheSameWaitOnTheSameInputsIsReportedOnce()
  {
    var (operation, log) = Operation();
    var load = Work(Guid.NewGuid());
    var pending = new RoutePlanningException(
      "Stop address verification is pending.",
      DateTime.UtcNow.AddHours(1)
    );

    for (var attempt = 0; attempt < 5; attempt++)
      operation.ReportWait(load, "inputs-1", Address, pending);

    var line = Assert.Single(log.Lines);
    Assert.Contains("address", line);
    Assert.Contains("inputs-1", line);
    Assert.Contains(Carrier.ToString(), line);
    Assert.Contains(load.DispatchId.ToString(), line);
  }

  [Fact]
  public void ANewReasonNewInputsOrASuccessReportAgain()
  {
    var (operation, log) = Operation();
    var load = Work(Guid.NewGuid());
    var address = new RoutePlanningException(
      "Stop address verification is pending."
    );
    var deadhead = new RoutePlanningException(
      "Deadhead preparation is pending."
    );

    operation.ReportWait(load, "inputs-1", Address, address);
    operation.ReportWait(load, "inputs-1", Deadhead, deadhead);
    operation.ReportWait(load, "inputs-2", Deadhead, deadhead);
    operation.ForgetWait(load);
    operation.ReportWait(load, "inputs-2", Deadhead, deadhead);

    Assert.Equal(4, log.Lines.Count);
    Assert.Contains("waits: deadhead;", log.Lines[1]);
  }

  // The same load id under two carriers is two loads.
  [Fact]
  public void EachCarriersLoadIsItsOwn()
  {
    var (operation, log) = Operation();
    var id = Guid.NewGuid();
    var failure = new RoutePlanningException(
      "Deadhead preparation is pending."
    );

    operation.ReportWait(Work(id), "inputs", Deadhead, failure);
    operation.ReportWait(Work(id, Guid.NewGuid()), "inputs", Deadhead, failure);

    Assert.Equal(2, log.Lines.Count);
  }

  // Bounded: past the capacity the load seen longest ago is forgotten, and
  // only reports once more.
  [Fact]
  public void TheRememberedWaitsStayWithinTheCapacity()
  {
    var (operation, log) = Operation(capacity: 2);
    var failure = new RoutePlanningException(
      "Deadhead preparation is pending."
    );
    var first = Work(Guid.NewGuid());
    var second = Work(Guid.NewGuid());
    var third = Work(Guid.NewGuid());

    operation.ReportWait(first, "i", Deadhead, failure);
    operation.ReportWait(second, "i", Deadhead, failure);
    operation.ReportWait(third, "i", Deadhead, failure);
    operation.ReportWait(third, "i", Deadhead, failure);
    operation.ReportWait(first, "i", Deadhead, failure);

    Assert.Equal(4, log.Lines.Count);
  }

  // A category from an allowlist, never the message: the step that stopped
  // the road, the busy and changed states, or unknown - no text, no digest
  // of it (root's review, September 27).
  [Fact]
  public void TheReasonIsACategoryAndNeverTheMessage()
  {
    var (operation, log) = Operation();
    var message = "Truck 11005 route through Somewhere, NY failed.";

    operation.ReportWait(
      Work(Guid.NewGuid()),
      "i",
      BaseRouteOperation.WaitStage.Inputs,
      new RoutePlanningException(message)
    );

    var line = Assert.Single(log.Lines);
    Assert.Contains("waits: unknown;", line);
    Assert.DoesNotContain("Somewhere", line);
    Assert.DoesNotMatch("[0-9a-f]{8};", line.Split("waits:")[1]);
    Assert.Equal(
      "road-provider",
      BaseRouteOperation.ReasonCode(Road, new RoutePlanningException(message))
    );
    Assert.Equal(
      "profile",
      BaseRouteOperation.ReasonCode(
        BaseRouteOperation.WaitStage.Profile,
        new RoutePlanningException(message)
      )
    );
    Assert.Equal(
      "inputs-busy",
      BaseRouteOperation.ReasonCode(
        Road,
        RoutePlanningException.InputsBusy(DateTime.UtcNow)
      )
    );
    Assert.Equal(
      "dependency-changed",
      BaseRouteOperation.ReasonCode(Road, RoutePlanningException.Changed("x"))
    );
  }

  private const BaseRouteOperation.WaitStage Address = BaseRouteOperation
    .WaitStage
    .Address;
  private const BaseRouteOperation.WaitStage Deadhead = BaseRouteOperation
    .WaitStage
    .Deadhead;
  private const BaseRouteOperation.WaitStage Road = BaseRouteOperation
    .WaitStage
    .Road;

  private static SourceRoadWork Work(Guid dispatch, Guid? company = null) =>
    new(
      dispatch,
      company ?? Carrier,
      null,
      1,
      Guid.NewGuid(),
      DateTime.UtcNow,
      3,
      false
    );

  private static (BaseRouteOperation, Recorder) Operation(int capacity = 100)
  {
    var options = Options.Create(
      new RoutePreparationOptions { StateCapacity = capacity }
    );
    var clock = new FakeTimeProvider();
    var log = new Recorder();
    var operation = new BaseRouteOperation(
      new ServiceCollection()
        .BuildServiceProvider()
        .GetRequiredService<IServiceScopeFactory>(),
      log,
      new RoutePreparationQueue(options, clock),
      options,
      clock
    );
    return (operation, log);
  }

  private sealed class Recorder : ILogger<BaseRouteOperation>
  {
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
      where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel,
      EventId eventId,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter
    ) => Lines.Add($"{logLevel}: {formatter(state, exception)}");
  }
}
