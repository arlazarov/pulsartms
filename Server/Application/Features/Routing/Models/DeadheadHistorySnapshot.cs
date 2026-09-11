using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Models;

public sealed record DeadheadHistorySnapshot(Load Current, IReadOnlyList<Load> Predecessors, bool HasUnknownStart);
