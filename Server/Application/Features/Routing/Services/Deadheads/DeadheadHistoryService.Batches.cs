using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Deadheads;

public sealed partial class DeadheadHistoryService
{
  public Task<
    IReadOnlyList<IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>>
  > ReadLoadedBatchesAsync(
    IReadOnlyList<IReadOnlyCollection<RouteWorkSnapshot>> batches,
    CancellationToken ct
  ) =>
    scope.ReadAsync<
      IReadOnlyList<IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>>
    >(
      async token =>
      {
        var result = new IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>[
          batches.Count
        ];
        var pending = Enumerable.Range(0, batches.Count).ToList();
        while (pending.Count > 0)
        {
          var ids = new HashSet<Guid>();
          var selected = new List<int>();
          foreach (var index in pending)
          {
            if (batches[index].Any(x => ids.Contains(x.Id)))
              continue;
            selected.Add(index);
            ids.UnionWith(batches[index].Select(x => x.Id));
          }
          var captured = selected
            .SelectMany(index => batches[index])
            .Select(RouteWorkProjection.TruckItinerary)
            .ToArray();
          var sources = await reader.ReadLoadedAsync(captured, token);
          var original = selected
            .Select(index =>
              (IReadOnlyDictionary<Guid, DeadheadHistorySource>)
                batches[index].ToDictionary(x => x.Id, x => sources[x.Id])
            )
            .ToArray();
          var applied = await ApplyExecutionBatchesAsync(original, token);
          for (var position = 0; position < selected.Count; position++)
          {
            var index = selected[position];
            result[index] = Freeze(applied[position]);
            pending.Remove(index);
          }
        }
        return result;
      },
      ct
    );
}
