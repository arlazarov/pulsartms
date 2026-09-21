namespace Domain.Models.Routing;

public sealed record PlanningRefreshWork(
  string Id,
  // The carrier this piece of work belongs to. A worker takes whatever is
  // next in the queue, whoever it belongs to, and then runs the pass as
  // them - so the answer has to travel with the work.
  Guid Company,
  PlanningScope Scope,
  long Version,
  Guid LeaseId,
  DateTime LeaseUntil,
  int Attempts
);

public sealed record PlanningRefreshState(bool Pending, DateTime AvailableAt);
