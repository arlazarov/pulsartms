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
      if (options.Value.Enabled)
        try
        {
          await RunOnceAsync(ct);
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
          TimeSpan.FromMinutes(
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

  public async Task<int> RunOnceAsync(CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
    var lookups =
      scope.ServiceProvider.GetRequiredService<FuelStationLookupService>();
    var now = clock.GetUtcNow().UtcDateTime;
    var stale = now.AddDays(-Math.Clamp(options.Value.RecheckDays, 1, 365));
    var today = DateOnly.FromDateTime(now);

    // Only stations somebody could actually be sent to: one with no current
    // price is not a candidate, and asking about it would spend the budget
    // that the candidates need.
    var due = await db
      .FuelStations.Where(x =>
        x.FuelDiscounts.Any(d =>
          d.EffectiveFrom <= today && d.EffectiveTo >= today
        ) && (x.StatusCheckedAt == null || x.StatusCheckedAt < stale)
      )
      .OrderBy(x => x.StatusCheckedAt == null ? 0 : 1)
      .ThenBy(x => x.StatusCheckedAt)
      .ThenBy(x => x.Id)
      .Take(Math.Clamp(options.Value.BatchSize, 1, 200))
      .ToListAsync(ct);
    if (due.Count == 0)
      return 0;

    var asked = 0;
    var closed = 0;
    foreach (var station in due)
    {
      ct.ThrowIfCancellationRequested();
      try
      {
        var place = await lookups.FindAsync(
          station.ExternalId,
          $"{station.Name}, {station.City}, {station.Region}",
          ct
        );
        // A provider that answers nothing has not said the station is shut.
        // Recording the attempt still matters, or the same station is asked
        // about again on every pass and no other station is ever reached.
        station.BusinessStatus =
          place?.BusinessStatus ?? station.BusinessStatus;
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
