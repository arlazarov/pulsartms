using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Fuel;
using Domain.Models.Fleet;
using Domain.Models.Messaging;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.FuelPlanning;

// The owner of what a driver has been given of their fuel plan.
//
// It reads the hand-overs of one truck's planned visits in one query when
// the plan is read for display - in the background, for the shared
// summary, never per card - and marks each stop with the latest hand-over
// of that visit, and whether the plan still says what was sent. It writes a
// hand-over only on an explicit confirmation, idempotently, and after the
// commit asks for that one truck's summary again.
public sealed class FuelIssueRecords(
  IAppDbContext db,
  PlanningSummaryCache summaries,
  ICurrentCompany company,
  IOptions<FuelIssueOptions> options,
  TimeProvider time,
  IPlanningPublicationScope publication
)
{
  private readonly FuelIssueWindow window = new(options, time);

  // What a fuel calculation saw of the truck's hand-overs when it began: the
  // latest one recorded, and the moment it began.
  public sealed record Stamp(Guid TruckId, DateTime At, DateTime? LatestSentAt);

  public async Task<Stamp> StampAsync(Guid truckId, CancellationToken ct) =>
    new(
      truckId,
      time.GetUtcNow().UtcDateTime,
      await LatestSentAtAsync(truckId, ct)
    );

  // Inside a fuel plan's publication, which holds the truck's publication
  // lock that RecordAsync takes too: a plan calculated before a hand-over,
  // or while a WhatsApp attempt for the truck is in flight or has changed
  // state (an acceptance not yet recorded as a hand-over), is refused and
  // calculated again, so it can never drop that hand-over unseen.
  public async Task RequireUnchangedAsync(Stamp stamp, CancellationToken ct)
  {
    var latest = await LatestSentAtAsync(stamp.TruckId, ct);
    var attempts = await db.DriverMessages.AnyAsync(
      x =>
        x.TruckId == stamp.TruckId
        && x.VisitKeys != ""
        && (x.Status == DriverMessageStatuses.Sending || x.StatusAt > stamp.At),
      ct
    );
    if (latest != stamp.LatestSentAt || attempts)
      throw new PlanningSettingsConflictException(
        "A fuel stop was handed to the driver during the calculation. "
          + "It is calculated again."
      );
  }

  private Task<DateTime?> LatestSentAtAsync(
    Guid truckId,
    CancellationToken ct
  ) =>
    db
      .FuelVisitSends.AsNoTracking()
      .Where(x => x.TruckId == truckId)
      .MaxAsync(x => (DateTime?)x.SentAt, ct);

  // The visits among these, still ahead in the saved plan, that the driver
  // holds: each one's latest hand-over, in the scope of the stop it comes
  // before, is this visit (whatever quantity it said).
  public async Task<IReadOnlyList<FuelHandedOver>> HandedOverAsync(
    TruckFuelPlanSnapshot saved,
    IReadOnlyCollection<FuelPlanEditStop> ahead,
    CancellationToken ct
  )
  {
    var stops = saved
      .Plan.Stops.Where(stop =>
        ahead.Any(x =>
          x.StationId == stop.StationId && x.BeforeStopId == stop.BeforeStopId
        )
      )
      .ToList();
    if (stops.Count == 0)
      return [];
    var dispatches = stops.Select(x => x.DispatchId).Distinct().ToArray();
    var sends = await db
      .FuelVisitSends.AsNoTracking()
      .Where(x =>
        x.TruckId == saved.TruckId && dispatches.Contains(x.DispatchId)
      )
      .Select(x => new
      {
        x.DispatchId,
        x.ScopeId,
        x.AssignmentRevision,
        x.StationId,
        x.BeforeStopId,
        x.SentAt,
      })
      .ToListAsync(ct);
    var held = new List<FuelHandedOver>();
    foreach (var stop in stops)
    {
      if (Scope(saved, stop) is not { } scope)
        continue;
      var latest = sends
        .Where(x =>
          x.DispatchId == stop.DispatchId
          && x.ScopeId == scope.Id
          && x.AssignmentRevision == scope.Revision
          && x.StationId == stop.StationId
          && x.BeforeStopId == stop.BeforeStopId
        )
        .MaxBy(x => x.SentAt);
      if (latest is not null)
        held.Add(
          new(
            stop.StationId,
            stop.BeforeStopId,
            stop.DispatchId,
            stop.Name,
            latest.SentAt
          )
        );
    }
    return held;
  }

  private sealed record Sent(
    Guid DispatchId,
    Guid ScopeId,
    long AssignmentRevision,
    Guid StationId,
    Guid BeforeStopId,
    string Content,
    DateTime SentAt,
    string? SentBy,
    string Channel,
    string? Delivery
  );

  public async Task ApplyAsync(
    TruckFuelPlanSnapshot saved,
    FuelPlan plan,
    DriverHosClocks? hos,
    CancellationToken ct
  )
  {
    var dispatches = plan.Stops.Select(x => x.DispatchId).Distinct().ToArray();
    var sends =
      dispatches.Length == 0
        ? []
        : await db
          .FuelVisitSends.AsNoTracking()
          .Where(x =>
            x.TruckId == saved.TruckId && dispatches.Contains(x.DispatchId)
          )
          .Select(x => new Sent(
            x.DispatchId,
            x.ScopeId,
            x.AssignmentRevision,
            x.StationId,
            x.BeforeStopId,
            x.Content,
            x.SentAt,
            x.SentBy,
            x.Channel,
            // In the same query: the carrying message's latest status.
            x.MessageId == null
              ? null
              : db
                .DriverMessages.Where(m => m.Id == x.MessageId)
                .Select(m => m.Status)
                .FirstOrDefault()
          ))
          .ToListAsync(ct);
    foreach (var stop in plan.Stops)
    {
      stop.Sent = null;
      if (Scope(saved, stop) is not { } scope)
        continue;
      // The latest hand-over of this visit is what the driver was last
      // told; a plan that now says anything else has changed since.
      var latest = sends
        .Where(x =>
          x.DispatchId == stop.DispatchId
          && x.ScopeId == scope.Id
          && x.AssignmentRevision == scope.Revision
          && x.StationId == stop.StationId
          && x.BeforeStopId == stop.BeforeStopId
        )
        .MaxBy(x => x.SentAt);
      if (latest is not null)
        stop.Sent = new(
          latest.SentAt,
          latest.SentBy,
          latest.Channel,
          latest.Content != FuelVisitIdentity.Content(stop)
        )
        {
          Delivery = latest.Delivery,
        };
    }
    // A withdrawn visit stays in view until the dispatcher hands the driver
    // something newer: that hand-over is the answer to it.
    if (plan.Withdrawn is { Count: > 0 } withdrawn)
    {
      var latestSent = await LatestSentAtAsync(saved.TruckId, ct);
      plan.Withdrawn = withdrawn
        .Where(x => latestSent is null || latestSent <= x.WithdrawnAt)
        .ToList();
    }
    window.Apply(plan, hos);
  }

  // Records that these visits, as the plan reads now, were passed on.
  // Returns how many were new; a visit already recorded with the same
  // content is the same hand-over and is not written twice. A provider
  // message that carried content already recorded becomes that hand-over's
  // latest carrier, so a resend after a failure is what the plan shows.
  //
  // Written under the truck's publication lock, which a fuel plan's
  // publication holds too, so the two are ordered. A confirmation by hand
  // (requireCurrent) is refused - null - when the saved plan is no longer
  // the version confirmed; a WhatsApp acceptance is recorded whatever the
  // plan says now, because the message has already gone.
  public async Task<int?> RecordAsync(
    TruckFuelPlanSnapshot saved,
    IReadOnlyList<(FuelPlanStop Stop, string Text)> visits,
    string channel,
    string? actor,
    CancellationToken ct,
    Guid? messageId = null,
    bool requireCurrent = false
  )
  {
    var owner =
      company.Id ?? throw new InvalidOperationException("A company is needed.");
    // Another planning pass may hold the lock for a moment; a hand-over,
    // perhaps of a message already sent, waits for it rather than failing.
    IDbContextTransaction? opened = null;
    for (var attempt = 1; opened is null; attempt++)
      try
      {
        opened = await publication.BeginAsync(saved.TruckId, ct);
      }
      catch (RoutePlanningException busy) when (busy.Busy && attempt < 10)
      {
        await Task.Delay(TimeSpan.FromMilliseconds(500), time, ct);
      }
    await using var transaction = opened;
    if (
      requireCurrent
      && await db
        .TruckFuelPlans.AsNoTracking()
        .Where(x => x.TruckId == saved.TruckId)
        .Select(x => (DateTime?)x.CalculatedAt)
        .FirstOrDefaultAsync(ct) != saved.CalculatedAt
    )
      return null;
    var now = time.GetUtcNow().UtcDateTime;
    var added = 0;
    var written = new List<FuelVisitSend>();
    foreach (var (stop, text) in visits)
    {
      if (Scope(saved, stop) is not { } scope)
        continue;
      var content = FuelVisitIdentity.Content(stop);
      var existing = await db.FuelVisitSends.FirstOrDefaultAsync(
        x =>
          x.TruckId == saved.TruckId
          && x.ScopeId == scope.Id
          && x.AssignmentRevision == scope.Revision
          && x.StationId == stop.StationId
          && x.BeforeStopId == stop.BeforeStopId
          && x.Content == content,
        ct
      );
      if (existing is not null)
      {
        if (messageId is null || existing.MessageId == messageId)
          continue;
        existing.MessageId = messageId;
        existing.Channel = channel;
        existing.SentAt = now;
        existing.SentBy = actor;
        written.Add(existing);
        added++;
        continue;
      }
      var row = new FuelVisitSend
      {
        Id = Guid.NewGuid(),
        CompanyId = owner,
        TruckId = saved.TruckId,
        DispatchId = stop.DispatchId,
        ExecutionLegId = scope.Leg,
        ScopeId = scope.Id,
        AssignmentRevision = scope.Revision,
        StationId = stop.StationId,
        BeforeStopId = stop.BeforeStopId,
        Content = content,
        Text = text.Length > 1000 ? text[..1000] : text,
        PlanCalculatedAt = saved.CalculatedAt,
        Channel = channel,
        SentAt = now,
        SentBy = actor,
        MessageId = messageId,
      };
      db.FuelVisitSends.Add(row);
      written.Add(row);
      added++;
    }
    if (added == 0)
      return 0;
    try
    {
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
    }
    catch (DbUpdateException)
    {
      // A confirmation that raced this one wrote the same hand-over first.
      // Nothing of this attempt stays pending on the context.
      foreach (var row in written)
        db.Entry(row).State = EntityState.Detached;
      return 0;
    }
    // After the commit, and for this truck only.
    summaries.Committed(owner, saved.TruckId);
    return added;
  }

  // The accepted work a visit belongs to: the leg and assignment of the
  // stop it comes before, as the saved plan captured them - not the plan's
  // first load, which moves on as loads are finished.
  private static (Guid Id, Guid? Leg, long Revision)? Scope(
    TruckFuelPlanSnapshot saved,
    FuelPlanStop stop
  ) =>
    saved
      .Stops.Where(x =>
        x.DispatchId == stop.DispatchId && x.Stop.Id == stop.BeforeStopId
      )
      .Select(x =>
        ((Guid, Guid?, long)?)
          (
            x.ExecutionLegId ?? x.DispatchId,
            x.ExecutionLegId,
            x.AssignmentRevision
          )
      )
      .FirstOrDefault();
}
