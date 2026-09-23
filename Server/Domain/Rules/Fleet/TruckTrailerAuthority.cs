namespace Domain.Rules.Fleet;

public static class TruckTrailerSources
{
  // The configured telemetry provider reported it.
  public const string Telemetry = "telemetry";

  // The truck's accepted, active execution leg names it.
  public const string Execution = "execution";

  // The truck's current imported load names it at its current stop.
  public const string Load = "load";
}

// What the telemetry provider says about a truck's trailer. Unknown - no
// current record, a feed that did not answer, a trailer it cannot place - is
// never a detach: only a provider saying the assignment ended is.
public readonly record struct TelemetryTrailer(bool Known, Guid? TrailerId)
{
  public static readonly TelemetryTrailer Unknown = new(false, null);
}

// What the truck's current work says: one trailer, none, or two loads that
// disagree.
public sealed record WorkTrailer(Guid? TrailerId, string Source, bool Ambiguous)
{
  public static readonly WorkTrailer None = new(
    null,
    TruckTrailerSources.Load,
    false
  );
}

public sealed record EffectiveTrailer(
  Guid? TrailerId,
  string? Source,
  Guid? ConflictId
);

// Which trailer a truck has now. The telemetry provider is preferred when
// it is certain; the current work stands in when it is not. Neither is
// overruled silently: a disagreement keeps the other side as a conflict.
public static class TruckTrailerAuthority
{
  public static EffectiveTrailer Resolve(
    TelemetryTrailer telemetry,
    WorkTrailer work
  )
  {
    var named = work.Ambiguous ? null : work.TrailerId;
    if (telemetry.Known)
      return new(
        telemetry.TrailerId,
        TruckTrailerSources.Telemetry,
        named is { } other && other != telemetry.TrailerId ? other : null
      );
    return named is { } trailer
      ? new(trailer, work.Source, null)
      : new(null, null, null);
  }

  // One trailer is on one truck. When two trucks resolve to the same one,
  // the truck whose telemetry reports it keeps it; if that does not settle
  // it, neither takes it. The loser keeps it as a conflict, never silently
  // losing the fact.
  public static IReadOnlyDictionary<Guid, EffectiveTrailer> Settle(
    IReadOnlyDictionary<Guid, EffectiveTrailer> trucks
  )
  {
    var settled = trucks.ToDictionary(x => x.Key, x => x.Value);
    foreach (
      var group in trucks
        .Where(x => x.Value.TrailerId.HasValue)
        .GroupBy(x => x.Value.TrailerId!.Value)
        .Where(x => x.Count() > 1)
    )
    {
      var reported = group
        .Where(x => x.Value.Source == TruckTrailerSources.Telemetry)
        .ToList();
      var keeper = reported.Count == 1 ? reported[0].Key : (Guid?)null;
      foreach (var (truck, _) in group)
        if (truck != keeper)
          settled[truck] = new(null, null, group.Key);
    }
    return settled;
  }
}
