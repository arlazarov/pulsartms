namespace Application.Features.Fuel.Models;

public sealed class GmailWatchState
{
  public DateTime RegisteredAt { get; set; }
  public DateTime? ExpiresAt { get; set; }
  public ulong? HistoryId { get; set; }
  public DateTime NextRenewalAt { get; set; }
  public DateTime NextRecoveryAt { get; set; }
  public DateTime? LastRenewedAt { get; set; }
  public DateTime? LastRecoveredAt { get; set; }
  public int RenewalFailures { get; set; }
  public int RecoveryFailures { get; set; }
  public string? RenewalErrorCode { get; set; }
  public string? RecoveryErrorCode { get; set; }
}
