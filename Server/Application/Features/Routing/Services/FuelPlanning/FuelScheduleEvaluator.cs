using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Eta.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Models;
using Application.Features.Routing.Exceptions;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class FuelScheduleEvaluator(IAppDbContext db, IDriverHosProvider hos,
    IHosHistoryProvider historyProvider, EtaService eta)
{
    public async Task<FuelScheduleContext> PrepareAsync(RoutePlanningState state, TruckRoute baseline,
        IReadOnlyList<FuelItineraryStop> itinerary, DateTime now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FuelScheduleContext.ValidateInputs(state, baseline, itinerary, now) is { } unavailable)
            return new(eta, state, baseline, itinerary, now, null, null, unavailable, cancellationToken);
        var driverId = await db.Trucks.AsNoTracking().Where(truck => truck.Id == state.Plan!.TruckId)
            .Select(truck => truck.Driver == null ? "" : truck.Driver.ExternalId).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(driverId))
            return new(eta, state, baseline, itinerary, now, null, null, "Driver HOS unavailable.", cancellationToken);
        DriverHosClocks? source;
        try { source = (await hos.GetClocksAsync(cancellationToken)).GetValueOrDefault(driverId); }
        catch (Exception error) when (Unavailable(error, cancellationToken))
        {
            return new(eta, state, baseline, itinerary, now, null, null, "Driver HOS temporarily unavailable.", cancellationToken);
        }
        var clocks = source is null ? null : new DriverHosClocks
        {
            BreakMs = source.BreakMs, DriveMs = source.DriveMs, ShiftMs = source.ShiftMs,
            CycleMs = source.CycleMs, UpdatedAt = source.UpdatedAt, CurrentDutyStatus = source.CurrentDutyStatus
        };
        HosHistory? history;
        try { history = await historyProvider.GetAsync(driverId, cancellationToken); }
        catch (Exception error) when (Unavailable(error, cancellationToken)) { history = null; }
        if (history is not null) history = history with { Periods = history.Periods.ToArray() };
        cancellationToken.ThrowIfCancellationRequested();
        return new(eta, state, baseline, itinerary, now, clocks, history, null, cancellationToken);
    }

    private static bool Unavailable(Exception error, CancellationToken cancellationToken) =>
        error is HttpRequestException or RoutePlanningException
        || error is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}
