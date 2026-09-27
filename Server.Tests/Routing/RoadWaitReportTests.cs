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
      operation.ReportWait(load, "inputs-1", pending);

    var line = Assert.Single(log.Lines);
    Assert.Contains("address-pending", line);
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

    operation.ReportWait(load, "inputs-1", address);
    operation.ReportWait(load, "inputs-1", deadhead);
    operation.ReportWait(load, "inputs-2", deadhead);
    operation.ForgetWait(load);
    operation.ReportWait(load, "inputs-2", deadhead);

    Assert.Equal(4, log.Lines.Count);
    Assert.Contains("deadhead-pending", log.Lines[1]);
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

    operation.ReportWait(Work(id), "inputs", failure);
    operation.ReportWait(Work(id, Guid.NewGuid()), "inputs", failure);

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

    operation.ReportWait(first, "i", failure);
    operation.ReportWait(second, "i", failure);
    operation.ReportWait(third, "i", failure);
    operation.ReportWait(third, "i", failure);
    operation.ReportWait(first, "i", failure);

    Assert.Equal(4, log.Lines.Count);
  }

  // A code, never the text: a planning message becomes a short fixed
  // digest, and the busy and changed states their own names.
  [Fact]
  public void TheReasonIsACodeAndNotTheMessage()
  {
    var (operation, log) = Operation();
    var message = "Truck 11005 route through Somewhere, NY failed.";

    operation.ReportWait(
      Work(Guid.NewGuid()),
      "i",
      new RoutePlanningException(message)
    );

    Assert.DoesNotContain("Somewhere", Assert.Single(log.Lines));
    Assert.Matches("planning-[0-9a-f]{8}", log.Lines[0]);
    Assert.Equal(
      BaseRouteOperation.ReasonCode(new RoutePlanningException(message)),
      BaseRouteOperation.ReasonCode(new RoutePlanningException(message))
    );
    Assert.Equal(
      "inputs-busy",
      BaseRouteOperation.ReasonCode(
        RoutePlanningException.InputsBusy(DateTime.UtcNow)
      )
    );
    Assert.Equal(
      "inputs-changed",
      BaseRouteOperation.ReasonCode(RoutePlanningException.Changed("x"))
    );
  }

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
