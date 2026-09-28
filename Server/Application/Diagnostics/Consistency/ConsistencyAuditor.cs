using System.Diagnostics;
using System.Text.Json;
using Domain.Entities.Consistency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Diagnostics.Consistency;

// Runs one bounded pass for a company: detection first, read-only, page by
// page from each rule's cursor, each page journaled before the cursor moves;
// then the separate recovery policy. Rules and repairs come from their
// owning modules through interfaces, so this coordinator knows no module's
// tables. Passes are serialized in-process, so a slower pass never applies
// its pages after a newer one; across instances the journal's open-finding
// index keeps one open occurrence per entity.
public sealed class ConsistencyAuditor(
  IServiceScopeFactory scopes,
  ConsistencySweeps sweeps,
  ConsistencyRecovery recovery,
  IOptions<ConsistencyAuditOptions> options,
  TimeProvider clock,
  ILogger<ConsistencyAuditor> logger
)
{
  public const int ReportLimit = 100;

  // Invariants this auditor does not check, so an empty report is not read
  // as covering them.
  public static readonly IReadOnlyList<string> NotChecked =
  [
    "ETA forecasts, route plans and fuel plans against the current "
      + "assignment revision",
    "another process' in-memory planning summaries (each process checks "
      + "only its own)",
    "cross-company rows in server-owned tables other than planning demand",
    "driver message delivery state and messaging windows",
    "trailer catalog and assignment authority",
    "held work: whether its physical obligations were in fact finished",
  ];

  private readonly SemaphoreSlim gate = new(1, 1);
  private readonly Dictionary<Guid, int> rotation = [];

  public sealed record PassResult(
    Guid PassId,
    int Pages,
    int Rows,
    int RepairsRequested,
    IReadOnlyList<string> Completed,
    IReadOnlyList<string> Continuing,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Failed
  );

  // The scope that yields the company context is owned here and disposed
  // with the pass, however it ends; the context is released first.
  public async Task<PassResult> RunAsync(Guid company, CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    using var serving = scope
      .ServiceProvider.GetService<ICurrentCompany>()
      ?.As(company);
    return await RunAsync(company, () => ConsistencyWork.Open(scopes), ct);
  }

  public async Task<PassResult> RunAsync(
    Guid company,
    Func<ConsistencyWork> open,
    CancellationToken ct
  )
  {
    var settings = options.Value;
    var passId = Guid.NewGuid();
    var elapsed = Stopwatch.StartNew();
    var budget = TimeSpan.FromSeconds(settings.BudgetSeconds);
    int pages = 0,
      rows = 0,
      repaired = 0;
    List<string> completed = [],
      continuing = [],
      skipped = [],
      failed = [];
    await gate.WaitAsync(ct);
    try
    {
      string[] ids;
      await using (var listing = open())
        ids =
        [
          .. listing.Rules.Select(x => x.Info.Id).Order(StringComparer.Ordinal),
        ];
      // Rotate the starting rule so a spent budget does not always skip the
      // same one.
      var start = rotation.GetValueOrDefault(company);
      rotation[company] = start + 1;
      for (var i = 0; i < ids.Length; i++)
      {
        var id = ids[(start + i) % ids.Length];
        if (elapsed.Elapsed >= budget)
        {
          skipped.Add(id);
          continue;
        }
        var more = true;
        try
        {
          await using var work = open();
          var rule = work.Rules.Single(x => x.Info.Id == id);
          var info = rule.Info;
          for (
            var page = 0;
            more && page < settings.MaxPagesPerRule && elapsed.Elapsed < budget;
            page++
          )
          {
            ct.ThrowIfCancellationRequested();
            var at = clock.GetUtcNow().UtcDateTime;
            var read = await rule.ReadAsync(
              new(
                company,
                at,
                sweeps.BeginPage(company, id, at),
                settings.PageSize,
                TimeSpan.FromMinutes(settings.PendingGraceMinutes)
              ),
              ct
            );
            pages++;
            rows += read.Observed.Count;
            more = read.More;
            Log(
              await work.Journal.RecordAsync(
                company,
                passId,
                info,
                at,
                read.Observed,
                ct
              )
            );
            if (sweeps.Advance(company, id, at, read) is { } sweep)
              Log(
                await work.Journal.ResolveAsync(
                  company,
                  passId,
                  id,
                  sweep,
                  at,
                  ct
                )
              );
          }
          (more ? continuing : completed).Add(id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          sweeps.Fail(
            company,
            id,
            clock.GetUtcNow().UtcDateTime,
            ex.GetType().Name
          );
          failed.Add(id);
          logger.LogWarning(
            ex,
            "Consistency audit pass {PassId} could not check {Rule} for "
              + "company {CompanyId}",
            passId,
            id,
            company
          );
        }
      }
      if (elapsed.Elapsed < budget)
        try
        {
          await using var work = open();
          repaired = await recovery.RunAsync(
            company,
            passId,
            work.Journal,
            work.Repairs,
            settings,
            clock.GetUtcNow().UtcDateTime,
            ct
          );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          failed.Add("recovery");
          logger.LogWarning(
            ex,
            "Consistency recovery in pass {PassId} failed for company "
              + "{CompanyId}",
            passId,
            company
          );
        }
    }
    finally
    {
      gate.Release();
    }
    return new(
      passId,
      pages,
      rows,
      repaired,
      completed,
      continuing,
      skipped,
      failed
    );
  }

  public async Task<ConsistencyAuditReport> ReportAsync(
    Guid company,
    IEnumerable<ConsistencyRuleInfo> rules,
    ConsistencyJournalReads journal,
    CancellationToken ct
  )
  {
    var now = clock.GetUtcNow().UtcDateTime;
    var coverage = sweeps.Coverage(
      company,
      rules,
      now,
      TimeSpan.FromMinutes(options.Value.IntervalMinutes * 3)
    );
    var settings = options.Value;
    return new(
      company,
      now,
      coverage.All(x => x.Coverage is "complete" or "sweeping")
        ? "complete"
        : "unknown",
      coverage,
      await journal.OpenCountAsync(company, ct),
      [.. (await journal.OpenAsync(company, ReportLimit, ct)).Select(View)],
      settings.RepairEnabled
        ? settings.RepairRules ?? ConsistencyAuditOptions.DefaultRepairRules
        : [],
      NotChecked
    );
  }

  public static ConsistencyFindingView View(ConsistencyFinding x) =>
    new(
      x.Id,
      x.Rule,
      x.RuleVersion,
      x.EntityKey,
      x.Occurrence,
      x.Condition,
      x.Severity,
      x.State,
      x.Versions,
      JsonSerializer.Deserialize<Dictionary<string, string>>(x.EvidenceJson)
        ?? [],
      x.FirstSeenAt,
      x.LastSeenAt,
      x.LastTransitionAt,
      x.ResolvedAt,
      x.RepairAttempts,
      x.LastRepairOutcome,
      x.Escalated
    );

  private void Log(IReadOnlyList<ConsistencyEvent> events)
  {
    foreach (var entry in events)
      if (entry.Kind == "resolved")
        logger.LogInformation(
          "Consistency finding {Kind} by pass {PassId}: {Rule} {EntityKey} "
            + "occurrence {Occurrence} for company {CompanyId}",
          entry.Kind,
          entry.PassId,
          entry.Rule,
          entry.EntityKey,
          entry.Occurrence,
          entry.CompanyId
        );
      else
        logger.LogWarning(
          "Consistency finding {Kind} by pass {PassId}: {Rule} {EntityKey} "
            + "occurrence {Occurrence} for company {CompanyId}",
          entry.Kind,
          entry.PassId,
          entry.Rule,
          entry.EntityKey,
          entry.Occurrence,
          entry.CompanyId
        );
  }
}

public sealed record ConsistencyFindingView(
  Guid Id,
  string Rule,
  int RuleVersion,
  string EntityKey,
  int Occurrence,
  string Condition,
  string Severity,
  string State,
  string Versions,
  IReadOnlyDictionary<string, string> Evidence,
  DateTime FirstSeenAt,
  DateTime LastSeenAt,
  DateTime LastTransitionAt,
  DateTime? ResolvedAt,
  int RepairAttempts,
  string? LastRepairOutcome,
  bool Escalated
);

// Status is "complete" only when every registered rule finished a sweep
// recently and its last page succeeded; otherwise "unknown", whatever the
// number of findings. No findings under "unknown" is not a clean fleet.
public sealed record ConsistencyAuditReport(
  Guid Company,
  DateTime GeneratedAt,
  string Status,
  IReadOnlyList<ConsistencyRuleCoverage> Rules,
  int OpenCount,
  IReadOnlyList<ConsistencyFindingView> Open,
  IReadOnlyList<string> RepairAllowlist,
  IReadOnlyList<string> NotChecked
);
