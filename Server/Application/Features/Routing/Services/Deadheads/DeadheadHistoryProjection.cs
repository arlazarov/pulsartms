using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Deadheads;

public static class DeadheadHistoryProjection
{
  public static DeadheadHistorySnapshot Capture(DeadheadHistorySource source)
  {
    var current = RouteWorkProjection.TruckItinerary(source.Current);
    var predecessors = source
      .Predecessors.Select(RouteWorkProjection.TruckItinerary)
      .OrderBy(x => x.Id)
      .ThenBy(x => x.ExecutionLegId)
      .ToImmutableArray();
    var signature = Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(
          new
          {
            Current = current,
            Predecessors = predecessors,
            source.HasUnknownStart,
          }
        )
      )
    );
    return new(current, predecessors, source.HasUnknownStart, signature);
  }
}
