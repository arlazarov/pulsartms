namespace Application.Features.Routing.Models;

public sealed record PlanningRefreshWork(
  string Id,
  PlanningScope Scope,
  long Version,
  Guid LeaseId,
  DateTime LeaseUntil,
  int Attempts
);

public sealed record PlanningRefreshState(bool Pending, DateTime AvailableAt);
