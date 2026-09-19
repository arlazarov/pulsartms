namespace Application.Features.Synchronization.Models;

public sealed record SynchronizationStatus(
  bool Enabled,
  bool Active,
  int PendingTrucks,
  Dictionary<string, SynchronizationJobStatus> Jobs
);

public sealed record SynchronizationJobStatus(
  DateTime? LastSuccess,
  DateTime NextRun,
  int Failures,
  string? Error
);
