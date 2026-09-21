using Domain.Entities.Dispatch;
using Domain.Models.Routing;

namespace Application.Features.Routing.Services.Deadheads;

// When an empty road between two loads has to be bought again. Every
// answer of "yes" here is a paid request to a routing provider, so both
// questions are asked in one place and named: there is nothing to buy, or
// what is already saved still answers.
public static class DeadheadFreshness
{
  // Nothing to buy: there is no pair of loads to connect, the profile that
  // would shape the road is not usable, no provider is configured, or the
  // load is not assigned to anyone yet.
  public static bool NothingToConnect(
    DeadheadConnection? pair,
    string hash,
    bool routingConfigured,
    string status
  ) =>
    pair is null
    || hash.Length == 0
    || !routingConfigured
    || status == "unassigned";

  // What is saved still answers: it was built from these same inputs, and
  // either its retry budget has not run out yet or it carries a road that
  // can still be read back.
  public static bool StillAnswers(
    DispatchDeadhead? saved,
    string hash,
    DeadheadConnection pair,
    TruckRouteProfile profile,
    DateTime now
  ) =>
    saved is not null
    && saved.InputHash == hash
    && (
      saved.RetryAfter > now
      || saved.Miles.HasValue && pair.ReadRoute(saved, profile) is not null
    );
}
