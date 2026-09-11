using Domain.Entities.Dispatch;

namespace Application.Features.Routing.Models;

public sealed record NextLoadRouteVersion(Guid DispatchId, string? BaseInputHash, DateTime? BaseCalculatedAt,
  Guid? PreviousDispatchId, string? DeadheadInputHash, DateTime? DeadheadCalculatedAt, decimal? EmptyMiles);

public sealed record SavedNextLoadRoute(Guid DispatchId, DispatchBaseRoute? BaseRoute, DispatchDeadhead? Deadhead);
