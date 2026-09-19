using Application.Behaviors;
using Application.Features.Routing.Models;
using Application.Features.Routing.Queries;
using Application.Models;
using Microsoft.Extensions.Logging;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RequestDiagnosticsTests
{
  [Fact]
  public async Task SuccessfulPollsRecordMetricsWithoutInformationLogs()
  {
    var logger = new CaptureLogger();
    var behavior = new RequestDiagnosticsBehavior<
      GetRoutePlanningQuery,
      RequestResponse<RoutePlanningState>
    >(logger);
    var count =
      RequestMetrics
        .Snapshot()
        .GetValueOrDefault(nameof(GetRoutePlanningQuery))
        ?.Count ?? 0;
    await behavior.Handle(
      new(Guid.NewGuid()),
      _ =>
        Task.FromResult(
          RequestResponse<RoutePlanningState>.Ok(
            new(new(), null, null, null, null, true)
          )
        ),
      default
    );
    Assert.True(
      RequestMetrics.Snapshot()[nameof(GetRoutePlanningQuery)].Count > count
    );
    Assert.Empty(logger.Levels);
  }

  [Fact]
  public async Task UnexpectedFailuresPropagateToTheBoundaryWithoutDuplicateLogging()
  {
    var logger = new CaptureLogger();
    var behavior = new RequestDiagnosticsBehavior<
      GetRoutePlanningQuery,
      RequestResponse<RoutePlanningState>
    >(logger);
    var failure = new InvalidOperationException("Test failure");
    var actual = await Assert.ThrowsAsync<InvalidOperationException>(
      () => behavior.Handle(new(Guid.NewGuid()), _ => throw failure, default)
    );
    Assert.Same(failure, actual);
    Assert.Empty(logger.Levels);
  }

  private sealed class CaptureLogger
    : ILogger<
      RequestDiagnosticsBehavior<
        GetRoutePlanningQuery,
        RequestResponse<RoutePlanningState>
      >
    >
  {
    public List<LogLevel> Levels { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
      where TState : notnull => null;

    public bool IsEnabled(LogLevel level) => true;

    public void Log<TState>(
      LogLevel level,
      EventId id,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter
    ) => Levels.Add(level);
  }
}
