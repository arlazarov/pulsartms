using Application.Features.Routing.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Server.Tests.Support;

internal sealed class PublicationProbe(AppDbContext db)
  : IPlanningPublicationScope
{
  public Func<Task>? BeforeBegin { get; set; }
  public int Calls { get; private set; }

  public async Task<IDbContextTransaction> BeginAsync(
    Guid? truckId,
    CancellationToken ct
  )
  {
    Calls++;
    if (BeforeBegin is { } before)
      await before();
    return await new PlanningPublicationScope(db).BeginAsync(truckId, ct);
  }
}
