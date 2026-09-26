using Application.Behaviors;
using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Application.Features.Routing.Queries;
using Application.Models;
using Domain.Models.Routing;
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

  // A long poll waits by design: an idle browser's answer every 20 seconds
  // is not a slow request. Logged as one, it was a line per browser every
  // 20 seconds. The same wait in an ordinary request still is logged.
  [Fact]
  public async Task AWaitByDesignIsNotLoggedAsSlowWorkIs()
  {
    var waiting =
      new CaptureLogger<
        RequestDiagnosticsBehavior<
          WaitMessagingChangesQuery,
          RequestResponse<MessagingChanges>
        >
      >();
    var working =
      new CaptureLogger<
        RequestDiagnosticsBehavior<
          GetRoutePlanningQuery,
          RequestResponse<RoutePlanningState>
        >
      >();

    await Task.WhenAll(
      new RequestDiagnosticsBehavior<
        WaitMessagingChangesQuery,
        RequestResponse<MessagingChanges>
      >(waiting).Handle(
        new(Guid.NewGuid()),
        async _ =>
        {
          await Task.Delay(TimeSpan.FromMilliseconds(1050));
          return RequestResponse<MessagingChanges>.Ok(
            new(Guid.NewGuid(), false, [])
          );
        },
        default
      ),
      new RequestDiagnosticsBehavior<
        GetRoutePlanningQuery,
        RequestResponse<RoutePlanningState>
      >(working).Handle(
        new(Guid.NewGuid()),
        async _ =>
        {
          await Task.Delay(TimeSpan.FromMilliseconds(1050));
          return RequestResponse<RoutePlanningState>.Ok(
            new(new(), null, null, null, null, true)
          );
        },
        default
      )
    );

    Assert.Empty(waiting.Levels);
    Assert.Equal([LogLevel.Information], working.Levels);
  }

  private sealed class CaptureLogger
    : CaptureLogger<
      RequestDiagnosticsBehavior<
        GetRoutePlanningQuery,
        RequestResponse<RoutePlanningState>
      >
    >;

  private class CaptureLogger<T> : ILogger<T>
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
