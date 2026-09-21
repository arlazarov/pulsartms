using Application.Behaviors;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Queries;
using Application.Models;
using Microsoft.Extensions.Logging;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class RequestLoggingScopeTests
{
  [Fact]
  public async Task EveryLineLoggedDuringARequestSaysWhichTruckItIsAbout()
  {
    var load = Guid.NewGuid();
    var recorder = new Recorder();
    var behavior = new RequestDiagnosticsBehavior<
      RecalculateFuelPlanCommand,
      RequestResponse<object>
    >(
      recorder.For<
        RequestDiagnosticsBehavior<
          RecalculateFuelPlanCommand,
          RequestResponse<object>
        >
      >()
    );

    await behavior.Handle(
      new RecalculateFuelPlanCommand(load),
      _ =>
      {
        recorder.Write("the fuel plan could not be built");
        return Task.FromResult(RequestResponse<object>.Ok(new()));
      },
      default
    );

    var line = Assert.Single(recorder.Lines);
    Assert.Equal("the fuel plan could not be built", line.Message);
    Assert.Equal("RecalculateFuelPlanCommand", line.Scope["Request"]);
    Assert.Equal(load, line.Scope["Load"]);
  }

  [Fact]
  public void PlanningRequestsSayWhatWorkTheyAreAbout()
  {
    var id = Guid.NewGuid();
    Assert.Equal(id, ((IAboutWork)new BuildRouteCommand(id, null!)).Load);
    Assert.Equal(id, ((IAboutWork)new GetTruckPlanningQuery(id)).Truck);
    // A request that is about nothing in particular still logs, it just
    // has nothing to add.
    Assert.Null(((IAboutWork)new GetTruckPlanningQuery(id)).Load);
  }

  private sealed record Line(
    string Message,
    IReadOnlyDictionary<string, object?> Scope
  );

  private sealed class Recorder
  {
    private readonly List<Dictionary<string, object?>> scopes = [];
    public List<Line> Lines { get; } = [];

    public ILogger<T> For<T>() => new Writer<T>(this);

    public void Write(string message) =>
      Lines.Add(
        new(
          message,
          scopes.Count == 0 ? new Dictionary<string, object?>() : scopes[^1]
        )
      );

    private sealed class Writer<T>(Recorder recorder) : ILogger<T>
    {
      public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
      {
        recorder.scopes.Add((Dictionary<string, object?>)(object)state);
        return new Pop(recorder);
      }

      public bool IsEnabled(LogLevel level) => true;

      public void Log<TState>(
        LogLevel level,
        EventId id,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
      ) => recorder.Write(formatter(state, exception));
    }

    private sealed class Pop(Recorder recorder) : IDisposable
    {
      public void Dispose() =>
        recorder.scopes.RemoveAt(recorder.scopes.Count - 1);
    }
  }
}
