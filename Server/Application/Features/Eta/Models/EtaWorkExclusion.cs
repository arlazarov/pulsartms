using Application.Features.Execution.Models;

namespace Application.Features.Eta.Models;

public enum EtaWorkExclusionReason
{
  LegacyNotAssigned,
  OverdueUpcoming,
  NativeNotActive,
  NativeConnectionRequired,
  UnresolvedWork,
  BlockedByEarlierWork,
  SavedRouteCompleted,
}

public sealed record EtaWorkExclusion(
  WorkIdentity Work,
  EtaWorkExclusionReason Reason
);
