using System.Data;
using Application.Caching;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;

namespace Application.Features.Routing.Services.Addresses;

public sealed class ExecutionStopAddressService(
  IAppDbContext db,
  IAddressGeocoder geocoder,
  ReadCache reads,
  RoutePreparationQueue preparation,
  TimeProvider clock
)
{
  public async Task VerifyAsync(Guid legId, CancellationToken ct)
  {
    if (db.Database.CurrentTransaction is not null)
      throw new InvalidOperationException(
        "Address resolution requires no active transaction."
      );
    var captured = await db
      .ExecutionLegs.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == legId, ct);
    if (captured is null || captured.Status is not ("active" or "planned"))
      return;
    var transfers = await ExecutionTransfers.ReadAsync(db, [captured], ct);
    var stops = ExecutionStopRows.Read(captured);
    var changed = false;
    var now = clock.GetUtcNow().UtcDateTime;
    foreach (var stop in stops)
    {
      if (
        transfers.ContainsKey(stop.Id)
        || !StopLocation.RequiresAddressVerification(stop)
        || StopLocation.VerifiedPoint(stop, now) is not null
        || stop.AddressRetryAfter > now
      )
        continue;
      await StopAddressResolution.ApplyAsync(stop, geocoder, now, ct);
      changed = true;
    }
    if (!changed)
      return;
    await using var transaction = await db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      ct
    );
    var leg = await db
      .ExecutionLegs.Include(x => x.Loads)
      .SingleAsync(x => x.Id == legId, ct);
    if (leg.Revision != captured.Revision || leg.Status != captured.Status)
      throw new DbUpdateConcurrencyException(
        "Execution changed during address resolution."
      );
    if (await ExecutionAcceptance.HasProtectedPathAsync(db, leg, stops, ct))
      throw new RoutePlanningException(
        "Recorded mileage protects this location. Review the accepted address."
      );
    await ExecutionAcceptance.ApplyAsync(
      db,
      [new(leg, stops)],
      "address-verified",
      null,
      null,
      now,
      ct
    );
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    foreach (
      var key in new[] { "dispatch", "board", "execution", "route-previews" }
    )
      reads.Invalidate(key);
    preparation.MarkTruckDirty(leg.TruckId);
    foreach (var link in leg.Loads)
      preparation.MarkDirty(link.DispatchId);
  }
}
