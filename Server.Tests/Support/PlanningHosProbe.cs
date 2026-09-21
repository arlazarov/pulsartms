using Application.Features.Fleet.Interfaces;
using Domain.Models.Fleet;
using Infrastructure.Persistence;

namespace Server.Tests.Support;

internal sealed class PlanningHosProbe(AppDbContext db) : IDriverHosProvider
{
  public Dictionary<string, DriverHosClocks> Clocks { get; } = [];
  public int Calls { get; private set; }
  public Action? BeforeRead { get; set; }

  public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
    CancellationToken ct
  )
  {
    Assert.Null(db.Database.CurrentTransaction);
    Calls++;
    BeforeRead?.Invoke();
    return Task.FromResult<IReadOnlyDictionary<string, DriverHosClocks>>(
      Clocks
    );
  }
}
