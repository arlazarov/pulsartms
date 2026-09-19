using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.DriverStatus.DriverNextRecap;

public partial class DriverNextRecap
{
  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Parameter]
  public DriverCycleSnapshot? Snapshot { get; set; }
  private DateTimeOffset? NextRecap =>
    Snapshot
      is {
        Cycle.RecapVerified: true,
        Cycle.NextRecapMinutes: > 0,
        Cycle.NextRecapAt: { } at
      }
    && Snapshot.CalculatedAt <= Clock.GetUtcNow().UtcDateTime
    && Snapshot.ValidUntil > Snapshot.CalculatedAt
    && Clock.GetUtcNow().UtcDateTime - Snapshot.ValidUntil
      < ArrivalDisplayMemory.PendingDisplayGrace
    && at > Clock.GetUtcNow()
      ? at
      : null;
}
