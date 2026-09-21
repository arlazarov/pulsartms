using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Deadheads;

// Making sure a load's empty connection to the load before it exists and
// is current. The work is reserved before any billable request goes out,
// so two workers never buy the same road twice, and a rolled-back attempt
// leaves nothing behind for a later one to pick up.
public sealed partial class DeadheadService
{
  public Task EnsureAsync(
    Load source,
    TruckRouteProfile profile,
    CancellationToken ct
  ) => EnsureAsync(RouteWorkProjection.Capture(source), profile, ct);

  public async Task EnsureAsync(
    RouteWorkSnapshot source,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    profile = profile.Copy();
    if (source.Status is not ("assigned" or "in_transit" or "unassigned"))
      return;
    var captured = await historyReader.ReadAsync(source, ct);
    var latestLoad = captured?.Current;
    if (
      latestLoad is null
      || latestLoad.Status is not ("assigned" or "in_transit" or "unassigned")
    )
      return;
    var load = latestLoad;
    var pair = DeadheadConnection.Find(captured);
    var hash =
      pair is not null && profile.Validate() is null
        ? pair.Signature(profile)
        : "";
    var gate = Gates.For(load.TruckId ?? load.Id);
    await GateWait.WaitAsync(gate, "Deadhead", ct);
    DispatchDeadhead? saved = null;
    var publishing = false;
    try
    {
      await using (
        var reservation = await publication.BeginAsync(captured!, ct)
      )
      {
        publishing = true;
        saved = await db.DispatchDeadheads.SingleOrDefaultAsync(
          x =>
            x.DispatchId == load.Id && x.ExecutionLegId == load.ExecutionLegId,
          ct
        );
        if (load.ExecutionLegId is null)
          await financials.SaveAsync(
            RateInputs(load),
            hash.Length > 0 && saved?.InputHash == hash ? saved.Miles : null,
            hash,
            ct
          );
        if (
          DeadheadFreshness.NothingToConnect(
            pair,
            hash,
            routing.IsConfigured,
            load.Status
          )
        )
        {
          await reservation.CommitAsync(ct);
          return;
        }
        var sameInputs = saved?.InputHash == hash;
        if (
          DeadheadFreshness.StillAnswers(
            saved,
            hash,
            pair,
            profile,
            DateTime.UtcNow
          )
        )
        {
          await reservation.CommitAsync(ct);
          return;
        }
        if (saved is null)
        {
          saved = new()
          {
            Id = Guid.NewGuid(),
            DispatchId = load.Id,
            ExecutionLegId = load.ExecutionLegId,
          };
          db.DispatchDeadheads.Add(saved);
        }
        saved.InputHash = hash;
        saved.PreviousDispatchId = pair.Previous.Id;
        saved.PreviousExecutionLegId = pair.Previous.ExecutionLegId;
        // Missing geometry must not erase valid financial mileage for the same
        // inputs.
        if (!sameInputs)
          saved.Miles = null;
        saved.RouteJson = null;
        if (!sameInputs)
          saved.CalculatedAt = null;
        // Persist the retry budget before billable provider requests.
        saved.ErrorMessage = null;
        saved.RetryAfter = DateTime.UtcNow.AddMinutes(5);
        await db.SaveChangesAsync(ct);
        await reservation.CommitAsync(ct);
      }
      publishing = false;
      TruckRoute route;
      try
      {
        var from = await StopLocation.ResolveAsync(pair.From, routing, ct);
        var to = await StopLocation.ResolveAsync(pair.To, routing, ct);
        route = await routing.CalculateAsync([from, to], profile, ct);
        if (!SavedRouteGeometry.Complete(route, 1))
          throw new RoutePlanningException(
            "Empty route geometry is incomplete.",
            saved.RetryAfter
          );
        if (!RouteAnchoring.Matches(route, [from, to]))
          throw new RoutePlanningException(
            "The empty route does not reach the confirmed stops.",
            saved.RetryAfter
          );
      }
      catch (RoutePlanningException ex)
      {
        saved.ErrorMessage = ex.Message;
        saved.RetryAfter = ex.RetryAfter;
        await db.SaveChangesAsync(ct);
        throw;
      }
      await using var transaction = await publication.BeginAsync(captured!, ct);
      publishing = true;
      if (pair!.Signature(profile) != hash)
        throw new RoutePlanningException("The connection inputs changed.");
      saved!.Miles = (decimal)route.Miles;
      saved.RouteJson = RoutePlanStorage.Serialize(route);
      saved.CalculatedAt = DateTime.UtcNow;
      saved.RetryAfter = DateTime.MinValue;
      saved.ErrorMessage = null;
      await db.SaveChangesAsync(ct);
      if (load.ExecutionLegId is null)
        await financials.SaveAsync(RateInputs(load), saved.Miles, hash, ct);
      await transaction.CommitAsync(ct);
    }
    catch (DbUpdateConcurrencyException ex)
      when (saved is not null
        && ex.Entries.Count > 0
        && ex.Entries.All(entry => ReferenceEquals(entry.Entity, saved))
      )
    {
      // Another worker owns the persisted reservation or its completed result.
      DetachResults(load.Id, saved);
    }
    catch when (publishing)
    {
      DetachResults(load.Id, saved);
      throw;
    }
    finally
    {
      gate.Release();
    }
  }

  private static DispatchRateInputs RateInputs(RouteWorkSnapshot load) =>
    new(load.Id, load.Price, load.LoadedMiles, load.Currency);

  private void DetachResults(Guid dispatchId, DispatchDeadhead? saved)
  {
    // Rolled-back values must not be reused or saved by a later operation.
    if (saved is not null)
      db.Entry(saved).State = EntityState.Detached;
    foreach (
      var rate in db
        .DispatchRates.Local.Where(x => x.DispatchId == dispatchId)
        .ToArray()
    )
      db.Entry(rate).State = EntityState.Detached;
  }
}
