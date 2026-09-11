using Application.Features.Fuel.Interfaces;

namespace Application.Features.Fuel.Models;

public sealed record GmailWatchRunResult(GmailWatchResult? Watch = null, bool Busy = false,
  string? RenewalError = null, string? RecoveryError = null);
