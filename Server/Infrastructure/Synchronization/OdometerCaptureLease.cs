using Application.Features.Mileage.Interfaces;
using Infrastructure.Persistence;

namespace Infrastructure.Synchronization;

public sealed class OdometerCaptureLease(AppDbContext db)
  : IOdometerCaptureLease
{
  private readonly CheckpointLeaseStore checkpoint = new(
    db,
    new Guid("566cd4e4-0c91-440f-a5e9-daf80e2e599d")
  );

  public Task<bool> AcquireAsync(
    string owner,
    DateTime now,
    CancellationToken ct
  ) => checkpoint.AcquireAsync(owner, now, ct);
}
