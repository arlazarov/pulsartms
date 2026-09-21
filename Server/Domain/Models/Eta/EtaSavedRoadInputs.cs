using System.Collections.Immutable;
using Domain.Models.Execution;
using Domain.Models.Routing;

namespace Domain.Models.Eta;

public sealed record EtaSavedRoadInputs(
  ImmutableArray<EtaRootRoadVersion> Roots,
  ImmutableArray<NextLoadRouteVersion> Future
);

public sealed record EtaRootRoadVersion(
  WorkIdentity Work,
  string Signature,
  string PlanSignature
);
