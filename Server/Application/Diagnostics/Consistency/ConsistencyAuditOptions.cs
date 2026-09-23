using System.ComponentModel.DataAnnotations;

namespace Application.Diagnostics.Consistency;

// Work limits for one audit pass of one company. A pass stops at whichever
// limit comes first and resumes from its cursor on the next pass, so a large
// backlog is covered over several passes rather than in one unbounded read.
public sealed class ConsistencyAuditOptions
{
  public bool Enabled { get; set; } = true;

  [Range(1, 1440)]
  public int IntervalMinutes { get; set; } = 10;

  [Range(1, 500)]
  public int PageSize { get; set; } = 100;

  [Range(1, 50)]
  public int MaxPagesPerRule { get; set; } = 5;

  [Range(1, 300)]
  public int BudgetSeconds { get; set; } = 20;

  // How long planning demand may stay unfinished before it is reported.
  // Demand retries with backoff indefinitely; this separates a slow queue
  // from one that is not converging.
  [Range(5, 1440)]
  public int PendingGraceMinutes { get; set; } = 30;

  // Recovery is separate from detection. Only rules listed here may have
  // their owner's repair requested; null means the built-in allowlist.
  public bool RepairEnabled { get; set; } = true;

  public string[]? RepairRules { get; set; }

  [Range(1, 10)]
  public int MaxRepairAttempts { get; set; } = 3;

  [Range(1, 1440)]
  public int RepairCooldownMinutes { get; set; } = 30;

  [Range(0, 50)]
  public int MaxRepairsPerPass { get; set; } = 5;

  public static readonly string[] DefaultRepairRules =
  [
    "routing.planning-refresh-overdue",
  ];
}
