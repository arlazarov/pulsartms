using Client.Models.DTO.Planning;

namespace Client.Services;

public sealed class ArrivalDisplayMemory
{
  public static readonly TimeSpan PendingDisplayGrace = TimeSpan.FromMinutes(15);
  public Guid? DispatchId { get; private set; }
  public PlanStop? Stop { get; private set; }
  private DispatchEta? previous;
  private bool completed;
  private DateTime? changedStopForecast;
  private bool awaitingChangedStop;

  public void Update(Guid? dispatchId, PlanStop? stop, DispatchEta? eta, bool completed = false)
  {
    this.completed = completed;
    if (completed)
    {
      DispatchId = dispatchId;
      Stop = null;
      previous = null;
      changedStopForecast = null;
      awaitingChangedStop = false;
      return;
    }
    if (previous is not null && DispatchId == dispatchId && Stop is { } existing && stop is not null && existing.Id == stop.Id
      && !SameStop(existing, stop))
    {
      changedStopForecast = previous.CalculatedAt;
      awaitingChangedStop = true;
      previous = null;
    }
    if (DispatchId != dispatchId || stop is not null && Stop?.Id != stop.Id)
    { previous = null; changedStopForecast = null; awaitingChangedStop = false; }
    if (DispatchId != dispatchId) Stop = null;
    DispatchId = dispatchId;
    if (stop is not null) Stop = stop;
    // A pending response is not a replacement for an already complete stop snapshot.
    if (Matches(eta) && (!awaitingChangedStop || !eta!.RouteUpdatePending
        && (changedStopForecast is null || eta.CalculatedAt > changedStopForecast))
      && (previous is null || eta!.CalculatedAt >= previous.CalculatedAt
        && (!eta.RouteUpdatePending || previous.RouteUpdatePending)))
    { previous = eta; awaitingChangedStop = false; }
  }

  public DispatchEta? PreviousDuringUpdate(DispatchEta? eta, DateTime now, bool refreshing = false) =>
    !completed && Stop is not null && previous is not null
      && (eta?.Stops.Count is not > 0 || Matches(eta))
      && (eta is null || eta.RouteUpdatePending || refreshing && Matches(eta))
      && now < previous.ValidUntil.ToUniversalTime().Add(PendingDisplayGrace) ? previous : null;

  public DispatchEta? Display(DispatchEta? eta, DateTime now, bool refreshing = false)
  {
    if (completed || awaitingChangedStop) return null;
    if (PreviousDuringUpdate(eta, now, refreshing) is { } retained) return retained;
    if (!Matches(eta)) return Stop is not null && eta is { Stops.Count: 0 }
      && eta.ValidUntil.ToUniversalTime() > now ? eta : null;
    var latest = previous is not null && previous.CalculatedAt > eta!.CalculatedAt ? previous : eta;
    return latest?.ValidUntil.ToUniversalTime() > now ? latest : null;
  }

  private static bool SameStop(PlanStop first, PlanStop second) => first.Address == second.Address
    && first.Point == second.Point && first.Sequence == second.Sequence && first.Job == second.Job
    && first.ScheduledDate == second.ScheduledDate && first.ScheduledTime == second.ScheduledTime
    && first.ScheduledDate2 == second.ScheduledDate2 && first.ScheduledTime2 == second.ScheduledTime2;

  private bool Matches(DispatchEta? eta) => Stop is { } stop && eta?.Stops.FirstOrDefault() is { } first
    && first.StopId == stop.Id
    && (DispatchId is null || first.DispatchId == DispatchId);
}
