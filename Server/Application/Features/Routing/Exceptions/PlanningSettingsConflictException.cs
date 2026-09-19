namespace Application.Features.Routing.Exceptions;

public sealed class PlanningSettingsConflictException(
  string message =
    "Settings were changed in another session. Reload the latest settings before saving."
) : Exception(message);
