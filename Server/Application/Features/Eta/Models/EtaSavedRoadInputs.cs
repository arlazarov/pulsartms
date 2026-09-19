using System.Collections.Immutable;
using Application.Features.Execution.Models;
using Application.Features.Routing.Models;

namespace Application.Features.Eta.Models;

public sealed record EtaSavedRoadInputs(
  ImmutableArray<EtaRootRoadVersion> Roots,
  ImmutableArray<NextLoadRouteVersion> Future
);

public sealed record EtaRootRoadVersion(
  WorkIdentity Work,
  string Signature,
  string PlanSignature
);
