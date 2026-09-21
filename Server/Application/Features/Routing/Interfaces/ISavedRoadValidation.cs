using Domain.Models.Routing;

namespace Application.Features.Routing.Interfaces;

public interface ISavedRoadValidation
{
  Task RequireCurrentAsync(
    IReadOnlyCollection<SavedRoadVersion> expected,
    CancellationToken ct
  );

  Task<bool> MatchesAsync(
    IReadOnlyCollection<SavedRoadVersion> expected,
    CancellationToken ct
  );
}
