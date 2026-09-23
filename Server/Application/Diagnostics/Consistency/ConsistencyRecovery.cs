using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Application.Diagnostics.Consistency;

// The recovery policy, separate from detection. It asks an allowlisted owner
// repair for open findings, at most MaxRepairsPerPass per pass, once per
// cooldown and MaxRepairAttempts times per occurrence, then escalates to an
// incident. Each attempt is journaled before the owner is asked. It resolves
// nothing: the detector's next complete sweep does that.
public sealed class ConsistencyRecovery(ILogger<ConsistencyRecovery> logger)
{
  public async Task<int> RunAsync(
    Guid company,
    Guid passId,
    ConsistencyJournal journal,
    IReadOnlyList<IConsistencyRepair> repairs,
    ConsistencyAuditOptions settings,
    DateTime now,
    CancellationToken ct
  )
  {
    if (!settings.RepairEnabled || settings.MaxRepairsPerPass == 0)
      return 0;
    var allowed = (
      settings.RepairRules ?? ConsistencyAuditOptions.DefaultRepairRules
    ).ToHashSet(StringComparer.Ordinal);
    var byRule = repairs
      .Where(x => allowed.Contains(x.Rule))
      .GroupBy(x => x.Rule, StringComparer.Ordinal)
      .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
    if (byRule.Count == 0)
      return 0;
    var requested = 0;
    var cooledBefore = now.AddMinutes(-settings.RepairCooldownMinutes);
    foreach (
      var candidate in await journal.RepairableAsync(
        company,
        byRule.Keys,
        cooledBefore,
        settings.MaxRepairsPerPass,
        ct
      )
    )
    {
      if (candidate.RepairAttempts >= settings.MaxRepairAttempts)
      {
        if (
          await journal.EscalateAsync(
            company,
            candidate.Id,
            settings.MaxRepairAttempts,
            passId,
            now,
            ct
          )
        )
          logger.LogWarning(
            "Consistency finding escalated by pass {PassId}: {Rule} "
              + "{EntityKey} after {Attempts} repair attempts for company "
              + "{CompanyId}",
            passId,
            candidate.Rule,
            candidate.EntityKey,
            candidate.RepairAttempts,
            company
          );
        continue;
      }
      var repair = byRule[candidate.Rule];
      // Re-checked under the journal lock: another pass may have repaired,
      // resolved or escalated it since the candidates were read.
      var finding = await journal.BeginRepairAsync(
        company,
        candidate.Id,
        repair.Action,
        passId,
        now,
        cooledBefore,
        settings.MaxRepairAttempts,
        ct
      );
      if (finding is null)
        continue;
      string outcome;
      try
      {
        outcome = await repair.RequestAsync(
          new(
            company,
            finding.EntityKey,
            JsonSerializer.Deserialize<Dictionary<string, string>>(
              finding.EvidenceJson
            ) ?? [],
            now
          ),
          ct
        );
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        outcome = ConsistencyRepairOutcome.Failed;
        logger.LogWarning(
          ex,
          "Consistency repair {Action} failed in pass {PassId} for {Rule} "
            + "{EntityKey}",
          repair.Action,
          passId,
          finding.Rule,
          finding.EntityKey
        );
      }
      await journal.EndRepairAsync(
        company,
        finding.Id,
        finding.RepairAttempts,
        outcome,
        passId,
        now,
        ct
      );
      requested++;
    }
    return requested;
  }
}
