using Domain.Models.Execution;

namespace Domain.Models.Eta;

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
