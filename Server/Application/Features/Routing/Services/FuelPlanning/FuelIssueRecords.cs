using Application.Features.Routing.Services.Routes;
using Domain.Entities.Fuel;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Routing;
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
  TimeProvider time
)
{
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
    FuelIssueHorizon.Apply(
      plan,
      hos,
      time.GetUtcNow(),
      TimeSpan.FromHours(options.Value.ShiftBufferHours),
      TimeSpan.FromMinutes(options.Value.HosFreshMinutes)
    );
  }

  // Records that these visits, as the plan reads now, were passed on.
  // Returns how many were new; a visit already recorded with the same
  // content is the same hand-over and is not written twice. A provider
  // message that carried content already recorded becomes that hand-over's
  // latest carrier, so a resend after a failure is what the plan shows.
  public async Task<int> RecordAsync(
    TruckFuelPlanSnapshot saved,
    IReadOnlyList<(FuelPlanStop Stop, string Text)> visits,
    string channel,
    string? actor,
    CancellationToken ct,
    Guid? messageId = null
  )
  {
    var owner =
      company.Id ?? throw new InvalidOperationException("A company is needed.");
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
