using System.Text.Json;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Options;
using Application.Features.Fuel.Services;
using Application.Interfaces;
using Domain.Entities.Fuel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Features.Fuel.Background;

public interface IFuelStationStatusOperation : IBackgroundOperation;

// The import asks the place provider about a station only when its name or
// location changed. A station that shut did neither, so the ones most likely
// to be closed are exactly the ones nothing was asking about - which is what
// puts a driver at a locked gate.
//
// This asks about them on a schedule instead: never-asked first, then the
// longest unanswered, a bounded number at a time because each question is
// billed.
public sealed class FuelStationStatusOperation(
  IServiceScopeFactory scopes,
  IOptions<FuelStationStatusOptions> options,
  TimeProvider clock,
  ILogger<FuelStationStatusOperation> logger
) : IFuelStationStatusOperation
{
  public async Task RunAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      var backlog = false;
      if (options.Value.Enabled)
        try
        {
          // A full pass means there is probably more waiting. While that is
          // true the next one follows in seconds, so the first sweep and any
          // newly imported station are done with in minutes.
          backlog =
            await RunOnceAsync(ct)
            >= Math.Clamp(options.Value.BatchSize, 1, 200);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          return;
        }
        catch (Exception ex)
        {
          logger.LogWarning(ex, "Fuel station statuses were not refreshed.");
        }
      try
      {
        await Task.Delay(
          backlog
            ? TimeSpan.FromSeconds(
              Math.Clamp(options.Value.BacklogSeconds, 1, 600)
            )
            : TimeSpan.FromMinutes(
              Math.Clamp(options.Value.IntervalMinutes, 1, 1440)
            ),
          clock,
          ct
        );
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }
  }

  // The stations the saved fuel plans currently send trucks to. There are a
  // handful of plans, so this is read whole and picked apart in memory.
  private static async Task<HashSet<Guid>> PlannedStationIdsAsync(
    IAppDbContext db,
    CancellationToken ct
  )
  {
    var ids = new HashSet<Guid>();
    foreach (
      var json in await db
        .TruckFuelPlans.AsNoTracking()
        .Select(x => x.SummaryJson)
        .ToListAsync(ct)
    )
    {
      if (string.IsNullOrWhiteSpace(json))
        continue;
      try
      {
        using var document = JsonDocument.Parse(json);
        if (
          !document.RootElement.TryGetProperty("plan", out var plan)
          || !plan.TryGetProperty("stops", out var stops)
          || stops.ValueKind != JsonValueKind.Array
        )
          continue;
        foreach (var stop in stops.EnumerateArray())
          if (
            stop.TryGetProperty("stationId", out var station)
            && station.TryGetGuid(out var id)
          )
            ids.Add(id);
      }
      catch (JsonException)
      {
        // A plan nobody can read names no station; the sweep still runs.
      }
    }
    return ids;
  }

  public async Task<int> RunOnceAsync(CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
    var lookups =
      scope.ServiceProvider.GetRequiredService<FuelStationLookupService>();
    var places =
      scope.ServiceProvider.GetRequiredService<IPlaceSearchService>();
    var now = clock.GetUtcNow().UtcDateTime;
    var stale = now.AddDays(-Math.Clamp(options.Value.RecheckDays, 1, 365));
    var today = DateOnly.FromDateTime(now);

    // Stations a driver is being sent to right now come first and are asked
    // about far more often. A station nobody is heading for can wait its turn
    // in a sweep that takes about a day; one in a live plan cannot, and
    // waiting a day is how two trucks were routed to a travel stop the
    // provider had already marked closed.
    // Stations are shared, but which of them a driver is being sent to is
    // read out of each carrier's own plans, so that part is asked once per
    // carrier and the answers are put together.
    var planned = new HashSet<Guid>();
    await CompanyPasses.ForEachCompanyAsync(
      scope.ServiceProvider,
      async token => planned.UnionWith(await PlannedStationIdsAsync(db, token)),
      ct
    );
    var plannedStale = now.AddMinutes(
      -Math.Clamp(options.Value.PlannedRecheckMinutes, 5, 1440)
    );
    var budget = Math.Clamp(options.Value.BatchSize, 1, 200);

    // Only stations somebody could actually be sent to: one with no current
    // price is not a candidate, and asking about it would spend the budget
    // that the candidates need.
    var due = await db
      .FuelStations.Where(x =>
        x.FuelDiscounts.Any(d =>
          d.EffectiveFrom <= today && d.EffectiveTo >= today
        )
        && (
          planned.Contains(x.Id)
            ? x.StatusCheckedAt == null || x.StatusCheckedAt < plannedStale
            : x.StatusCheckedAt == null || x.StatusCheckedAt < stale
        )
      )
      .OrderBy(x => planned.Contains(x.Id) ? 0 : 1)
      .ThenBy(x => x.StatusCheckedAt == null ? 0 : 1)
      .ThenBy(x => x.StatusCheckedAt)
      .ThenBy(x => x.Id)
      .Take(budget)
      .ToListAsync(ct);
    if (due.Count == 0)
      return 0;

    var asked = 0;
    var closed = 0;
    var changed = false;
    foreach (var station in due)
    {
      ct.ThrowIfCancellationRequested();
      try
      {
        // A station already identified is re-read by that identifier. Only
        // one nobody has placed yet is searched for by name.
        var place = string.IsNullOrWhiteSpace(station.PlaceId)
          ? await lookups.FindAsync(
            station.ExternalId,
            $"{station.Name}, {station.City}, {station.Region}",
            ct
          )
          : await places.ReadAsync(station.PlaceId, ct);
        // A provider that answers nothing has not said the station is shut.
        // Recording the attempt still matters, or the same station is asked
        // about again on every pass and no other station is ever reached.
        var was = station.BusinessStatus;
        station.BusinessStatus =
          place?.BusinessStatus ?? station.BusinessStatus;
        changed |= !string.Equals(
          was,
          station.BusinessStatus,
          StringComparison.Ordinal
        );
        if (place is not null)
        {
          if (!string.IsNullOrWhiteSpace(place.PlaceId))
            station.PlaceId = place.PlaceId;
          station.OpeningHoursJson = place.OpeningHoursJson;
          station.UtcOffsetMinutes = place.UtcOffsetMinutes;
        }
        station.StatusCheckedAt = clock.GetUtcNow().UtcDateTime;
        asked++;
        if (FuelStationStatus.Closed(station.BusinessStatus))
          closed++;
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        // One station's provider failure must not stop the pass; the lookup
        // keeps its own backoff for the station that failed.
        logger.LogWarning(
          ex,
          "Station {Station} status could not be refreshed.",
          station.ExternalId
        );
      }
    }
    await db.SaveChangesAsync(ct);
    // A station that just closed must leave the map and the planner at once.
    // Both read the cached station list, and nothing else drops it when a
    // status changes, so it would keep being offered until the entry aged
    // out on its own.
    if (changed)
      scope.ServiceProvider.GetRequiredService<IReadCache>().Invalidate("fuel");
    if (asked > 0)
      logger.LogInformation(
        "FuelStationStatus Asked={Asked} Closed={Closed} Due={Due}",
        asked,
        closed,
        due.Count
      );
    return asked;
  }
}
