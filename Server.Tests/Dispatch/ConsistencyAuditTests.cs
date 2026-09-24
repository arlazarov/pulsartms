using Application.Diagnostics.Consistency;
using Application.Features.Execution.Audit;
using Application.Features.Execution.Commands;
using Application.Features.Routing.Audit;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Consistency;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Mileage;
using Domain.Rules;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Truck = Domain.Entities.Fleet.Truck;

namespace Server.Tests.Dispatch;

// The runtime consistency audit: detection journaled before any repair,
// resolution only by a later complete sweep, recurrence as one incident,
// bounded pages resumed from a cursor, and history that survives a restart.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class ConsistencyAuditTests
{
  private const string Runnable = "execution.cancelled-source-runnable";
  private const string Held = "execution.cancelled-source-held";
  private const string Planning = "execution.planning-change-overdue";
  private const string Refresh = "routing.planning-refresh-overdue";

  // AMF1399 as production holds it: cancelled at the source and here, its
  // leg still planned with the old review text and a recorded movement.
  [Fact]
  public async Task LegacyCancelledWorkIsDetectedRepairedByItsOwnerAndKeptInReview()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var audit = new Audit(f);
    var truck = await TruckAsync(f);
    var source = Source("1399", "assigned");
    f.Sources.Add(source);
    await SyncAsync(f);
    var leg = await LegAsync(f);
    f.Db.Movements.Add(Movement(truck.Id, leg.Id));
    await f.Db.SaveChangesAsync();
    source.Status = "cancelled";
    await f.Db.Dispatches.ExecuteUpdateAsync(x =>
      x.SetProperty(d => d.Status, "cancelled")
    );
    await f.Db.ExecutionLegs.ExecuteUpdateAsync(x =>
      x.SetProperty(
        l => l.SourceReviewReason,
        "The source cancelled this load while its execution has started."
      )
    );
    f.Db.ChangeTracker.Clear();

    await audit.PassAsync();
    var runnable = Assert.Single(await OpenAsync(f));
    Assert.Equal(
      (Runnable, "violation", "critical"),
      (runnable.Rule, runnable.Condition, runnable.Severity)
    );
    Assert.Contains("\"legStatus\":\"planned\"", runnable.EvidenceJson);

    // The owner repairs it: ordinary synchronization holds the work.
    await SyncAsync(f);
    Assert.Equal(SourceCancellation.Held, (await LegAsync(f)).Status);
    await audit.PassAsync();
    var open = await OpenAsync(f);
    Assert.Equal(
      (Held, "review"),
      (open.Single().Rule, open.Single().Condition)
    );
    Assert.Equal(
      "resolved",
      (
        await f.Db.ConsistencyFindings.SingleAsync(x => x.Rule == Runnable)
      ).State
    );

    // An unchanged world writes nothing new.
    var events = await f.Db.ConsistencyEvents.CountAsync();
    await audit.PassAsync();
    Assert.Equal(events, await f.Db.ConsistencyEvents.CountAsync());

    // A dispatcher closes it; the next sweep verifies and resolves the review.
    leg = await LegAsync(f);
    var load = await f.Db.Dispatches.AsNoTracking().SingleAsync();
    var closed = await CloseAsync(f, load.Id, leg);
    Assert.True(closed.Success, string.Join(";", closed.Errors ?? []));
    await audit.PassAsync();
    Assert.Empty(await OpenAsync(f));
    Assert.Single(await f.Db.Movements.AsNoTracking().ToListAsync());
    Assert.Equal(
      [
        $"{Held} opened",
        $"{Held} resolved",
        $"{Runnable} opened",
        $"{Runnable} resolved",
      ],
      (
        await f
          .Db.ConsistencyEvents.Select(x => x.Rule + " " + x.Kind)
          .ToListAsync()
      ).Order()
    );
  }

  [Fact]
  public async Task ARecurrenceAfterAVerifiedResolutionIsOneIncident()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var audit = new Audit(f);
    var change = await ChangeAsync(f, audit.Clock);

    for (var round = 1; round <= 3; round++)
    {
      await audit.PassAsync();
      Assert.Equal(round, (await OpenAsync(f)).Single().Occurrence);
      await SetCompletedAsync(f, change, audit.Clock.GetUtcNow().UtcDateTime);
      audit.Clock.Advance(TimeSpan.FromMinutes(1));
      await audit.PassAsync();
      Assert.Empty(await OpenAsync(f));
      await SetCompletedAsync(f, change, null);
      audit.Clock.Advance(TimeSpan.FromMinutes(1));
    }

    var incident = await f.Db.ConsistencyIncidents.SingleAsync();
    Assert.Equal(
      (Planning, change.ToString(), "recurred", 2),
      (incident.Rule, incident.EntityKey, incident.Reason, incident.Recurrences)
    );
    Assert.Equal(
      ["incident-opened", "incident-updated"],
      await f
        .Db.ConsistencyEvents.Where(x => x.Kind.StartsWith("incident"))
        .OrderBy(x => x.Id)
        .Select(x => x.Kind)
        .ToListAsync()
    );
  }

  [Fact]
  public async Task APartialSweepResumesFromItsCursorAndResolvesNothing()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var audit = new Audit(f, x => (x.PageSize, x.MaxPagesPerRule) = (1, 1));
    var created = new List<Guid>();
    for (var i = 0; i < 3; i++)
      created.Add(await ChangeAsync(f, audit.Clock));
    Guid[] changes = [.. created.Order()];
    var reads = new List<int>();

    for (var pass = 0; pass < 3; pass++)
    {
      f.Counter.Reset();
      await audit.PassAsync(db => [new ExecutionPlanningDemandRule(db)]);
      reads.Add(
        f.Counter.Sql.Count(x =>
          x.Contains("FROM \"ExecutionPlanningChanges\"")
        )
      );
      if (pass == 0)
      {
        // Fixed while the sweep is still under way: absent from later pages
        // of this sweep is not proof, so it stays open.
        await SetCompletedAsync(
          f,
          changes[0],
          audit.Clock.GetUtcNow().UtcDateTime
        );
        var report = await audit.ReportAsync();
        Assert.Equal("unknown", report.Status);
        Assert.Equal(1, report.OpenCount);
      }
      audit.Clock.Advance(TimeSpan.FromMinutes(1));
    }

    Assert.All(reads, x => Assert.Equal(1, x));
    Assert.Equal(3, (await OpenAsync(f)).Count);
    Assert.Equal(
      changes.Select(x => x.ToString()).Order(),
      (
        await f.Db.ConsistencyFindings.Select(x => x.EntityKey).ToListAsync()
      ).Order()
    );

    // Only the next complete sweep, begun after it was last seen, resolves it.
    for (var pass = 0; pass < 2; pass++)
    {
      await audit.PassAsync(db => [new ExecutionPlanningDemandRule(db)]);
      audit.Clock.Advance(TimeSpan.FromMinutes(1));
    }
    Assert.Equal(
      [changes[1].ToString(), changes[2].ToString()],
      (await OpenAsync(f)).Select(x => x.EntityKey).Order()
    );
  }

  [Fact]
  public async Task ARestartForgetsCoverageButNotHistory()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var before = new Audit(f);
    var clock = before.Clock;
    await ChangeAsync(f, clock);
    await before.PassAsync();

    var after = new Audit(f, clock: clock);
    var report = await after.ReportAsync();
    Assert.Equal("unknown", report.Status);
    Assert.All(report.Rules, x => Assert.Equal("never-run", x.Coverage));
    Assert.Equal(1, report.OpenCount);

    clock.Advance(TimeSpan.FromMinutes(1));
    await after.PassAsync();
    Assert.Equal(1, await f.Db.ConsistencyFindings.CountAsync());
    Assert.Equal(
      1,
      await f.Db.ConsistencyEvents.CountAsync(x => x.Kind == "opened")
    );
    Assert.Equal("complete", (await after.ReportAsync()).Status);
  }

  [Fact]
  public async Task ALateObservationNeitherRewindsNorResolvesANewerOne()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var journal = new ConsistencyJournal(f.Db);
    var rule = new ExecutionPlanningDemandRule(f.Db).Info;
    var t0 = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    ConsistencyObservation Seen(string versions) =>
      new("entity", versions, new Dictionary<string, string>());

    await journal.RecordAsync(
      Company.Amf,
      Guid.NewGuid(),
      rule,
      t0.AddMinutes(2),
      [Seen("new")],
      default
    );
    await journal.RecordAsync(
      Company.Amf,
      Guid.NewGuid(),
      rule,
      t0,
      [Seen("old")],
      default
    );
    var resolved = await journal.ResolveAsync(
      Company.Amf,
      Guid.NewGuid(),
      rule.Id,
      new(t0.AddMinutes(1), new HashSet<string>()),
      t0.AddMinutes(3),
      default
    );

    Assert.Empty(resolved);
    var finding = await f.Db.ConsistencyFindings.AsNoTracking().SingleAsync();
    Assert.Equal(
      ("open", "new", t0.AddMinutes(2)),
      (finding.State, finding.Versions, finding.LastSeenAt)
    );
  }

  [Fact]
  public async Task AnotherCompanysRowsAndFindingsAreNeitherReadNorReported()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var audit = new Audit(f);
    var other = Guid.NewGuid();
    var now = audit.Clock.GetUtcNow().UtcDateTime;
    f.Db.ExecutionPlanningChanges.Add(
      new()
      {
        CompanyId = other,
        DispatchId = Guid.NewGuid(),
        TruckId = Guid.NewGuid(),
        ExecutionLegId = Guid.NewGuid(),
        RequestedAt = now.AddHours(-3),
        AvailableAt = now.AddHours(-3),
      }
    );
    await f.Db.SaveChangesAsync();
    await f.Db.Database.ExecuteSqlInterpolatedAsync(
      $"""
      INSERT INTO "PlanningRefreshRequests" ("Id", "CompanyId", "DispatchId",
        "AssignmentRevision", "InputSignature", "RequestedVersion",
        "CompletedVersion", "RequestedAt", "AvailableAt", "Attempts")
      VALUES ('other', {other}, {Guid.NewGuid()}, 1, '', 2, 1,
        {now.AddHours(-3)}, {now.AddHours(-3)}, 0)
      """
    );
    await f.Db.Database.ExecuteSqlInterpolatedAsync(
      $"""
      INSERT INTO "ConsistencyFindings" ("Id", "CompanyId", "Rule",
        "RuleVersion", "EntityKey", "Occurrence", "State", "Condition",
        "Severity", "Versions", "EvidenceJson", "FirstSeenAt", "LastSeenAt",
        "LastTransitionAt", "RepairAttempts", "Escalated")
      VALUES ({Guid.NewGuid()}, {other}, {Planning}, 1, 'theirs', 1, 'open',
        'violation', 'warning', '', '{"{}"}', {now}, {now}, {now}, 0, 0)
      """
    );

    await audit.PassAsync();

    var report = await audit.ReportAsync();
    Assert.Equal((0, "complete"), (report.OpenCount, report.Status));
    Assert.Equal(
      1,
      await f.Db.ConsistencyFindings.IgnoreQueryFilters().CountAsync()
    );
    Assert.Empty(await f.Db.ConsistencyEvents.ToListAsync());
    await f.Db.Database.ExecuteSqlInterpolatedAsync(
      $"""
      INSERT INTO "ConsistencyEvents" ("CompanyId", "Sequence", "FindingId",
        "Kind", "Rule", "EntityKey", "Occurrence", "PassId", "At",
        "DetailJson")
      VALUES ({other}, 1, {Guid.NewGuid()}, 'opened', {Planning}, 'theirs', 1,
        {Guid.NewGuid()}, {now.AddHours(-1)}, '{"{}"}')
      """
    );
    Assert.Empty(
      await new ConsistencyJournalReads(f.Db).EventsAsync(
        Company.Amf,
        0,
        100,
        null,
        default
      )
    );
  }

  // Overdue planning demand: the finding is journaled first, then the owner's
  // requeue is requested within cooldown and attempt limits, then escalated.
  // A successful requeue is not a resolution; only a later sweep is.
  [Fact]
  public async Task RepairIsAnAllowlistedRequeueThatIsNotResolutionAndEscalates()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var audit = new Audit(f);
    var now = audit.Clock.GetUtcNow().UtcDateTime;
    await ChangeAsync(f, audit.Clock);
    await f.Db.Database.ExecuteSqlInterpolatedAsync(
      $"""
      INSERT INTO "PlanningRefreshRequests" ("Id", "CompanyId", "DispatchId",
        "AssignmentRevision", "InputSignature", "RequestedVersion",
        "CompletedVersion", "RequestedAt", "AvailableAt", "Attempts")
      VALUES ('demand', {Company.Amf}, {Guid.NewGuid()}, 1, '', 2, 1,
        {now.AddHours(-3)}, {now.AddHours(3)}, 7)
      """
    );

    var first = await audit.PassAsync();
    Assert.Equal(1, first.RepairsRequested);
    Assert.Equal(
      ["opened", "opened", "repair-requested", "repair-requested"],
      await Kinds(f)
    );
    Assert.Equal(now, await AvailableAsync(f));
    var demand = await f.Db.ConsistencyFindings.SingleAsync(x =>
      x.Rule == Refresh
    );
    Assert.Equal(
      ("open", "requested", 1),
      (demand.State, demand.LastRepairOutcome, demand.RepairAttempts)
    );
    // The execution planning finding is not on the allowlist.
    Assert.Equal(
      0,
      (
        await f.Db.ConsistencyFindings.SingleAsync(x => x.Rule == Planning)
      ).RepairAttempts
    );

    // Within the cooldown nothing is asked again.
    audit.Clock.Advance(TimeSpan.FromMinutes(5));
    Assert.Equal(0, (await audit.PassAsync()).RepairsRequested);

    for (var attempt = 2; attempt <= 3; attempt++)
    {
      audit.Clock.Advance(TimeSpan.FromMinutes(31));
      Assert.Equal(1, (await audit.PassAsync()).RepairsRequested);
    }
    audit.Clock.Advance(TimeSpan.FromMinutes(31));
    await audit.PassAsync();
    demand = await f
      .Db.ConsistencyFindings.AsNoTracking()
      .SingleAsync(x => x.Rule == Refresh);
    Assert.Equal((3, true), (demand.RepairAttempts, demand.Escalated));
    var incident = await f.Db.ConsistencyIncidents.SingleAsync();
    Assert.Equal(
      ("repair-exhausted", 0),
      (incident.Reason, incident.Recurrences)
    );
    audit.Clock.Advance(TimeSpan.FromMinutes(31));
    Assert.Equal(0, (await audit.PassAsync()).RepairsRequested);

    // The owner finishes; the next complete sweep verifies it.
    await f.Db.Database.ExecuteSqlRawAsync(
      "UPDATE \"PlanningRefreshRequests\" SET \"CompletedVersion\" = 2"
    );
    audit.Clock.Advance(TimeSpan.FromMinutes(1));
    await audit.PassAsync();
    Assert.Equal(
      "resolved",
      (
        await f
          .Db.ConsistencyFindings.AsNoTracking()
          .SingleAsync(x => x.Rule == Refresh)
      ).State
    );
  }

  // A writer that has taken journal sequences but not yet committed holds
  // back every later writer, however long it takes, so a reader resuming
  // from its last sequence can never have passed an event that commits late.
  [Fact]
  public async Task AnEventCommittedLateIsNeverBehindAReadersCursor()
  {
    var path = Path.Combine(
      Path.GetTempPath(),
      $"journal-{Guid.NewGuid():N}.db"
    );
    try
    {
      // Each writer waits for the database lock, as a PostgreSQL writer
      // waits for the journal head row.
      AppDbContext Context(params IInterceptor[] interceptors)
      {
        var connection = new SqliteConnection(
          $"Data Source={path};Pooling=False"
        );
        connection.Open();
        using (var wait = connection.CreateCommand())
        {
          wait.CommandText = "PRAGMA busy_timeout = 20000";
          wait.ExecuteNonQuery();
        }
        return new(
          new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection, x => x.CommandTimeout(20))
            .AddInterceptors(interceptors)
            .Options
        );
      }
      await using (var setup = Context())
      {
        await setup.Database.EnsureCreatedAsync();
        // A waiting writer must not hold a read lock the first one needs to
        // commit; write-ahead logging gives the same shape as a row lock.
        await setup.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL");
      }
      var hold = new HoldBeforeCommit();
      var rule = new ExecutionPlanningDemandRule(null!).Info;
      var at = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
      ConsistencyObservation Seen(string key) =>
        new(key, "v", new Dictionary<string, string>());

      await using var first = Context(hold);
      await using var second = Context();
      await using var reader = Context();
      // SQLite runs "async" calls synchronously; each writer gets a thread.
      var early = Task.Run(
        () =>
          new ConsistencyJournal(first).RecordAsync(
            Company.Amf,
            Guid.NewGuid(),
            rule,
            at,
            [Seen("early")],
            default
          )
      );
      await hold.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
      var late = Task.Run(
        () =>
          new ConsistencyJournal(second).RecordAsync(
            Company.Amf,
            Guid.NewGuid(),
            rule,
            at,
            [Seen("late")],
            default
          )
      );
      await Task.Delay(500);
      Assert.False(late.IsCompleted, late.Exception?.ToString());
      var reads = new ConsistencyJournalReads(reader);
      Assert.Empty(await reads.EventsAsync(Company.Amf, 0, 10, null, default));

      hold.Release.SetResult();
      await Task.WhenAll(early, late);
      var events = await reads.EventsAsync(Company.Amf, 0, 10, null, default);
      Assert.Equal(
        [(1L, "early"), (2L, "late")],
        events.Select(x => (x.Sequence, x.EntityKey))
      );
      // A consumer that failed before checkpointing reads the same page.
      Assert.Equal(
        events.Select(x => x.Sequence),
        (await reads.EventsAsync(Company.Amf, 0, 10, null, default)).Select(x =>
          x.Sequence
        )
      );
      Assert.Empty(await reads.EventsAsync(Company.Amf, 2, 10, null, default));
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      File.Delete(path);
    }
  }

  // A failed save leaves changes to rows that already existed in its
  // context. They must die with that unit of work, not be written by the
  // next operation of the same pass.
  [Fact]
  public async Task AFailedSaveIsNotWrittenByTheNextOperation()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var audit = new Audit(f);
    var now = audit.Clock.GetUtcNow().UtcDateTime;
    await ChangeAsync(f, audit.Clock);
    await RefreshDemandAsync(f, now);
    IReadOnlyList<IConsistencyRule> Two(AppDbContext db) =>
      [
        new ExecutionPlanningDemandRule(db),
        new PlanningRefreshDemandRule(new PlanningRefreshStore(db)),
      ];
    await audit.PassAsync(Two);
    var seen = await f
      .Db.ConsistencyFindings.AsNoTracking()
      .ToDictionaryAsync(x => x.Rule, x => x.LastSeenAt);

    var failing = new FailOnModifiedFinding(Refresh);
    audit.Interceptors.Add(failing);
    audit.Clock.Advance(TimeSpan.FromMinutes(31));
    var pass = await audit.PassAsync(Two, mayFail: true);

    Assert.True(failing.Failed);
    Assert.Equal([Refresh], pass.Failed);
    var after = await f
      .Db.ConsistencyFindings.AsNoTracking()
      .ToDictionaryAsync(x => x.Rule, x => x);
    Assert.Equal(seen[Refresh], after[Refresh].LastSeenAt);
    Assert.True(after[Planning].LastSeenAt > seen[Planning]);
    // Recovery ran in its own unit after the failure and saved its attempt.
    Assert.Equal(2, after[Refresh].RepairAttempts);
    var report = await audit.ReportAsync();
    Assert.Equal("unknown", report.Status);
    Assert.Equal(
      "failed",
      report.Rules.Single(x => x.Rule == Refresh).Coverage
    );
  }

  [Fact]
  public async Task IncidentsWithTheSameTimestampAllArriveAcrossPages()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var at = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    for (var i = 0; i < 3; i++)
      f.Db.ConsistencyIncidents.Add(
        new()
        {
          Id = Guid.NewGuid(),
          CompanyId = Company.Amf,
          Rule = Planning,
          EntityKey = $"entity-{i}",
          Reason = "recurred",
          OpenedAt = at,
          LastAt = at,
        }
      );
    await f.Db.SaveChangesAsync();
    var reads = new ConsistencyJournalReads(f.Db);

    var seen = new List<Guid>();
    Guid? after = null;
    for (var page = 0; page < 5; page++)
    {
      var rows = await reads.IncidentsAsync(Company.Amf, after, 1, default);
      if (rows.Count == 0)
        break;
      seen.Add(rows[0].Id);
      after = rows[0].Id;
    }

    Assert.Equal(3, seen.Distinct().Count());
  }

  // An old pass read the finding, a newer pass observed it again and
  // committed, then the old pass resolves from its sweep. The old context
  // still tracks the stale row; the decision must use the committed one.
  [Fact]
  public async Task AStaleResolveDoesNotCloseANewerObservation()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    await using var oldContext = f.NewContext();
    await using var newContext = f.NewContext();
    var old = new ConsistencyJournal(oldContext);
    var rule = new ExecutionPlanningDemandRule(null!).Info;
    var t = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    ConsistencyObservation Seen() =>
      new("entity", "v", new Dictionary<string, string>());
    await old.RecordAsync(
      Company.Amf,
      Guid.NewGuid(),
      rule,
      t,
      [Seen()],
      default
    );
    await oldContext.ConsistencyFindings.ToListAsync();

    await new ConsistencyJournal(newContext).RecordAsync(
      Company.Amf,
      Guid.NewGuid(),
      rule,
      t.AddMinutes(3),
      [Seen()],
      default
    );
    var resolved = await old.ResolveAsync(
      Company.Amf,
      Guid.NewGuid(),
      rule.Id,
      new(t.AddMinutes(2), new HashSet<string>()),
      t.AddMinutes(4),
      default
    );

    Assert.Empty(resolved);
    var finding = await f.Db.ConsistencyFindings.AsNoTracking().SingleAsync();
    Assert.Equal(
      ("open", t.AddMinutes(3)),
      (finding.State, finding.LastSeenAt)
    );
  }

  [Fact]
  public async Task AStaleRepairCandidateLosesToTheAttemptAlreadyMade()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    await using var firstContext = f.NewContext();
    await using var secondContext = f.NewContext();
    var first = new ConsistencyJournal(firstContext);
    var second = new ConsistencyJournal(secondContext);
    var rule = new PlanningRefreshDemandRule(null!).Info;
    var t = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    await first.RecordAsync(
      Company.Amf,
      Guid.NewGuid(),
      rule,
      t,
      [new("demand", "v", new Dictionary<string, string>())],
      default
    );
    var cooled = t.AddMinutes(-30);
    var stale = Assert.Single(
      await first.RepairableAsync(Company.Amf, [rule.Id], cooled, 5, default)
    );

    var won = await second.BeginRepairAsync(
      Company.Amf,
      stale.Id,
      "requeue",
      Guid.NewGuid(),
      t,
      cooled,
      3,
      default
    );
    var lost = await first.BeginRepairAsync(
      Company.Amf,
      stale.Id,
      "requeue",
      Guid.NewGuid(),
      t,
      cooled,
      3,
      default
    );

    Assert.NotNull(won);
    Assert.Null(lost);
    Assert.Equal(
      1,
      (
        await f.Db.ConsistencyFindings.AsNoTracking().SingleAsync()
      ).RepairAttempts
    );
    Assert.Equal(
      1,
      await f.Db.ConsistencyEvents.CountAsync(x => x.Kind == "repair-requested")
    );

    // A second attempt began after the cooldown; the first attempt's late
    // outcome is journaled but does not overwrite the second's state.
    var next = await second.BeginRepairAsync(
      Company.Amf,
      stale.Id,
      "requeue",
      Guid.NewGuid(),
      t.AddMinutes(40),
      t.AddMinutes(10),
      3,
      default
    );
    Assert.Equal(2, next!.RepairAttempts);
    await first.EndRepairAsync(
      Company.Amf,
      stale.Id,
      1,
      ConsistencyRepairOutcome.Requested,
      Guid.NewGuid(),
      t.AddMinutes(41),
      default
    );
    var finding = await f.Db.ConsistencyFindings.AsNoTracking().SingleAsync();
    Assert.Equal(
      (2, "pending"),
      (finding.RepairAttempts, finding.LastRepairOutcome)
    );
  }

  private sealed class HoldBeforeCommit : SaveChangesInterceptor
  {
    public TaskCompletionSource Reached { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<int> SavedChangesAsync(
      SaveChangesCompletedEventData eventData,
      int result,
      CancellationToken cancellationToken = default
    )
    {
      Reached.TrySetResult();
      await Release.Task;
      return result;
    }
  }

  private sealed class FailOnModifiedFinding(string rule)
    : SaveChangesInterceptor
  {
    public bool Failed { get; private set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        !Failed
        && eventData
          .Context!.ChangeTracker.Entries<ConsistencyFinding>()
          .Any(x => x.State == EntityState.Modified && x.Entity.Rule == rule)
      )
      {
        Failed = true;
        throw new InvalidOperationException("Simulated lost connection.");
      }
      return ValueTask.FromResult(result);
    }
  }

  // A pass owns every scope it opens, the one that sets the company too,
  // and disposes each whether the pass completes, fails or is cancelled.
  [Theory]
  [InlineData("completes")]
  [InlineData("fails")]
  [InlineData("cancelled")]
  public async Task APassDisposesEveryScopeItOpens(string outcome)
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var services = new ServiceCollection();
    services.AddSingleton<ICurrentCompany>(new TestCompany());
    services.AddScoped<IAppDbContext>(_ => f.NewContext());
    services.AddScoped(sp =>
      outcome == "fails"
        ? throw new InvalidOperationException("Simulated failure.")
        : new ConsistencyJournal(sp.GetRequiredService<IAppDbContext>())
    );
    await using var provider = services.BuildServiceProvider();
    var scopes = new CountedScopes(
      provider.GetRequiredService<IServiceScopeFactory>()
    );
    var auditor = new ConsistencyAuditor(
      scopes,
      new ConsistencySweeps(),
      new ConsistencyRecovery(NullLogger<ConsistencyRecovery>.Instance),
      Options.Create(new ConsistencyAuditOptions()),
      TimeProvider.System,
      NullLogger<ConsistencyAuditor>.Instance
    );
    using var cancel = new CancellationTokenSource();
    if (outcome == "cancelled")
      cancel.Cancel();

    var error = await Record.ExceptionAsync(
      () => auditor.RunAsync(Company.Amf, cancel.Token)
    );

    Assert.Equal(outcome == "completes", error is null);
    Assert.True(scopes.Created > 0);
    Assert.Equal(scopes.Created, scopes.Disposed);
  }

  private sealed class CountedScopes(IServiceScopeFactory inner)
    : IServiceScopeFactory
  {
    public int Created { get; private set; }
    public int Disposed { get; private set; }

    public IServiceScope CreateScope()
    {
      Created++;
      return new Counted(inner.CreateScope(), () => Disposed++);
    }

    private sealed class Counted(IServiceScope scope, Action disposed)
      : IServiceScope,
        IAsyncDisposable
    {
      public IServiceProvider ServiceProvider => scope.ServiceProvider;

      public void Dispose()
      {
        disposed();
        scope.Dispose();
      }

      public ValueTask DisposeAsync()
      {
        disposed();
        return ((IAsyncDisposable)scope).DisposeAsync();
      }
    }
  }

  private static Task RefreshDemandAsync(DispatchSyncFixture f, DateTime now) =>
    f.Db.Database.ExecuteSqlInterpolatedAsync(
      $"""
      INSERT INTO "PlanningRefreshRequests" ("Id", "CompanyId", "DispatchId",
        "AssignmentRevision", "InputSignature", "RequestedVersion",
        "CompletedVersion", "RequestedAt", "AvailableAt", "Attempts")
      VALUES ('demand', {Company.Amf}, {Guid.NewGuid()}, 1, '', 2, 1,
        {now.AddHours(-3)}, {now.AddHours(3)}, 7)
      """
    );

  private sealed class Audit
  {
    private readonly DispatchSyncFixture f;
    private readonly ConsistencyAuditor auditor;
    public ManualTimeProvider Clock { get; }
    public List<IInterceptor> Interceptors { get; } = [];

    public Audit(
      DispatchSyncFixture fixture,
      Action<ConsistencyAuditOptions>? configure = null,
      ManualTimeProvider? clock = null
    )
    {
      f = fixture;
      Clock = clock ?? new ManualTimeProvider(DateTimeOffset.UtcNow);
      var settings = new ConsistencyAuditOptions();
      configure?.Invoke(settings);
      auditor = new(
        null!,
        new ConsistencySweeps(),
        new ConsistencyRecovery(NullLogger<ConsistencyRecovery>.Instance),
        Options.Create(settings),
        Clock,
        NullLogger<ConsistencyAuditor>.Instance
      );
    }

    public static IReadOnlyList<IConsistencyRule> Rules(AppDbContext db) =>
      [
        new CancelledSourceRunnableRule(db),
        new CancelledSourceHeldRule(db),
        new ExecutionPlanningDemandRule(db),
        new PlanningRefreshDemandRule(new PlanningRefreshStore(db)),
      ];

    public async Task<ConsistencyAuditor.PassResult> PassAsync(
      Func<AppDbContext, IReadOnlyList<IConsistencyRule>>? rules = null,
      bool mayFail = false
    )
    {
      var result = await auditor.RunAsync(
        Company.Amf,
        () =>
        {
          var db = f.NewContext([.. Interceptors]);
          return new ConsistencyWork(
            (rules ?? Rules)(db),
            [new PlanningRefreshRequeue(new PlanningRefreshStore(db))],
            new ConsistencyJournal(db),
            db
          );
        },
        default
      );
      if (!mayFail)
        Assert.Empty(result.Failed);
      f.Db.ChangeTracker.Clear();
      // Each pass is a later moment, as it is in production.
      Clock.Advance(TimeSpan.FromSeconds(1));
      return result;
    }

    public Task<ConsistencyAuditReport> ReportAsync() =>
      auditor.ReportAsync(
        Company.Amf,
        Rules(f.Db).Select(x => x.Info),
        new ConsistencyJournalReads(f.Db),
        default
      );
  }

  private static Task<List<ConsistencyFinding>> OpenAsync(
    DispatchSyncFixture f
  ) =>
    f
      .Db.ConsistencyFindings.AsNoTracking()
      .Where(x => x.State == "open")
      .ToListAsync();

  private static Task<List<string>> Kinds(DispatchSyncFixture f) =>
    f.Db.ConsistencyEvents.OrderBy(x => x.Id).Select(x => x.Kind).ToListAsync();

  private static Task<DateTime> AvailableAsync(DispatchSyncFixture f) =>
    f
      .Db.Database.SqlQueryRaw<DateTime>(
        "SELECT \"AvailableAt\" AS \"Value\" FROM \"PlanningRefreshRequests\""
      )
      .SingleAsync();

  private static async Task<Guid> ChangeAsync(
    DispatchSyncFixture f,
    TimeProvider clock
  )
  {
    var now = clock.GetUtcNow().UtcDateTime;
    var change = new ExecutionPlanningChange
    {
      CompanyId = Company.Amf,
      DispatchId = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      ExecutionLegId = Guid.NewGuid(),
      RequestedAt = now.AddHours(-2),
      AvailableAt = now.AddHours(-2),
      Attempts = 4,
    };
    f.Db.ExecutionPlanningChanges.Add(change);
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return change.Id;
  }

  private static Task SetCompletedAsync(
    DispatchSyncFixture f,
    Guid change,
    DateTime? at
  ) =>
    f
      .Db.ExecutionPlanningChanges.Where(x => x.Id == change)
      .ExecuteUpdateAsync(x => x.SetProperty(c => c.CompletedAt, at));

  private static async Task<Truck> TruckAsync(DispatchSyncFixture f)
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "11005",
      ExternalId = "11005",
      IsActive = true,
    };
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    return truck;
  }

  private static Application.Features.Dispatch.Models.ExternalDispatch Source(
    string number,
    string status
  ) =>
    new()
    {
      ExternalId = number,
      LoadNumber = int.Parse(number),
      Status = status,
      TruckNumber = "11005",
      Stops = new[] { "Pick Up", "Delivery" }
        .Select(
          (job, i) =>
            new Application.Features.Dispatch.Models.ExternalDispatchStop
            {
              Sequence = i + 1,
              Job = job,
              Name = $"{number} visit {i}",
              Address = $"{i + 1} Main Road",
              City = "Syracuse",
              Province = "NY",
              Country = "US",
              TruckNumber = "11005",
            }
        )
        .ToList(),
    };

  private static Movement Movement(Guid truck, Guid leg) =>
    new()
    {
      Id = Guid.NewGuid(),
      IdempotencyKey = Guid.NewGuid(),
      TruckId = truck,
      ExecutionLegId = leg,
      Origin = "telemetry",
      StartedAt = DateTime.UtcNow.AddHours(-29),
      RecordedAt = DateTime.UtcNow,
      RecordedBy = Guid.NewGuid(),
    };

  private static async Task SyncAsync(DispatchSyncFixture f)
  {
    f.Memory.Compact(1);
    f.Db.ChangeTracker.Clear();
    var result = await f.Handler.Handle(new(), default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    f.Db.ChangeTracker.Clear();
  }

  private static Task<ExecutionLeg> LegAsync(DispatchSyncFixture f) =>
    f.Db.ExecutionLegs.AsNoTracking().SingleAsync();

  private static async Task<Application.Models.RequestResponse<Application.Features.Execution.Models.ExecutionSourceApplyResult>> CloseAsync(
    DispatchSyncFixture f,
    Guid load,
    ExecutionLeg leg
  )
  {
    var user = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "dispatcher",
      Name = "Dispatcher",
      Email = "dispatcher@example.invalid",
    };
    f.Db.Users.Add(user);
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return await new CloseCancelledExecutionHandler(
      f.Db,
      new Caller(),
      new Roles(),
      TimeProvider.System,
      f.Reads,
      TestCache.Preparation()
    ).Handle(new(load, new(leg.Id, leg.Revision, Guid.NewGuid())), default);
  }

  private sealed class Caller : Application.Interfaces.ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => "dispatcher";
  }

  private sealed class Roles : Application.Interfaces.IUserRoleService
  {
    public Task<string?> GetAsync(string identityId, CancellationToken ct) =>
      Task.FromResult<string?>("Dispatch");

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string identityId,
      string role,
      CancellationToken ct
    ) => throw new NotSupportedException();
  }
}
