using Application.Interfaces;

namespace Server.Tests.Support;

// The dispatcher's chosen driver group as a test sets it: all drivers
// unless given one.
internal sealed class TestDriverScope(DriverScope? scope = null) : IDriverScope
{
  public DriverScope Scope { get; set; } = scope ?? DriverScope.All;

  public Task<DriverScope> CurrentAsync(CancellationToken ct) =>
    Task.FromResult(Scope);
}
