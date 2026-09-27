using System.Security.Cryptography;
using System.Text;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.Extensions.Logging;

namespace Application.Features.Routing.Background;

// Why a load's road waits (audit D1, F1): loads 1341 and 1355 retried for
// days and nothing said why. Each time a load's reason or inputs change, one
// Information line names the reason as a code; the same reason again on the
// same inputs says nothing, so a load retrying every few minutes logs once.
// Retries and their times are untouched: this only reports them.
public sealed partial class BaseRouteOperation
{
  private readonly object waitsLock = new();
  private readonly Dictionary<(Guid Company, Guid Dispatch), Wait> waits = [];
  private long waitsSeen;

  private sealed record Wait(string Signature, string Reason, long Seen);

  // A code, never the text: the texts are the application's own, but a code
  // is what a reader can count and search, and no message can carry more
  // than it should into the log.
  internal static string ReasonCode(RoutePlanningException failure) =>
    failure.Busy ? "inputs-busy"
    : failure.DependencyChanged ? "inputs-changed"
    : failure.Message switch
    {
      "Deadhead preparation is pending." => "deadhead-pending",
      "Stop address verification is pending." => "address-pending",
      var text => "planning-"
        + Convert
          .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8]
          .ToLowerInvariant(),
    };

  internal void ReportWait(
    SourceRoadWork work,
    string signature,
    RoutePlanningException failure
  )
  {
    var reason = ReasonCode(failure);
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
      "Route preparation for {DispatchId} waits: {Reason}; retry {RetryAfter}; attempt {Attempts}",
      work.DispatchId,
      reason,
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
