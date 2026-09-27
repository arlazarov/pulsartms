using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.Extensions.Logging;

namespace Application.Features.Routing.Background;

// Why a load's road waits (audit D1, F1): loads 1341 and 1355 retried for
// days and nothing said why. Each time a load's reason or inputs change, one
// Information line names the reason as a category from an allowlist - the
// step that stopped it - with the carrier and the input signature; the same
// reason again on the same inputs says nothing, so a load retrying every few
// minutes logs once.
// Retries and their times are untouched: this only reports them.
public sealed partial class BaseRouteOperation
{
  private readonly object waitsLock = new();
  private readonly Dictionary<(Guid Company, Guid Dispatch), Wait> waits = [];
  private long waitsSeen;

  private sealed record Wait(string Signature, string Reason, long Seen);

  // Where preparation stopped: the owner of the step that raised the wait.
  internal enum WaitStage
  {
    Inputs,
    Address,
    Assignment,
    Profile,
    Road,
    Deadhead,
  }

  // A category from an allowlist, never the message: the step that stopped
  // the road, or the exception's own busy and changed states. A step that
  // is not one of these says unknown.
  internal static string ReasonCode(
    WaitStage stage,
    RoutePlanningException failure
  ) =>
    failure.Busy ? "inputs-busy"
    : failure.DependencyChanged ? "dependency-changed"
    : stage switch
    {
      WaitStage.Address => "address",
      WaitStage.Assignment => "assignment",
      WaitStage.Profile => "profile",
      WaitStage.Road => "road-provider",
      WaitStage.Deadhead => "deadhead",
      _ => "unknown",
    };

  internal void ReportWait(
    SourceRoadWork work,
    string signature,
    WaitStage stage,
    RoutePlanningException failure
  )
  {
    var reason = ReasonCode(stage, failure);
    var key = (work.Company, work.DispatchId);
    lock (waitsLock)
    {
      var seen = ++waitsSeen;
      if (
        waits.TryGetValue(key, out var last)
        && last.Signature == signature
        && last.Reason == reason
      )
      {
        waits[key] = last with { Seen = seen };
        return;
      }
      waits[key] = new(signature, reason, seen);
      // Bounded: the load seen longest ago is forgotten first; forgotten,
      // its next wait is reported once more, which is all it costs.
      if (waits.Count > options.Value.StateCapacity)
        waits.Remove(waits.MinBy(x => x.Value.Seen).Key);
    }
    logger.LogInformation(
      "Route preparation for {DispatchId} of {CompanyId} waits: {Reason}; inputs {InputSignature}; retry {RetryAfter}; attempt {Attempts}",
      work.DispatchId,
      work.Company,
      reason,
      signature,
      failure.RetryAfter == DateTime.MaxValue ? null : failure.RetryAfter,
      work.Attempts
    );
  }

  internal void ForgetWait(SourceRoadWork work)
  {
    lock (waitsLock)
      waits.Remove((work.Company, work.DispatchId));
  }
}
