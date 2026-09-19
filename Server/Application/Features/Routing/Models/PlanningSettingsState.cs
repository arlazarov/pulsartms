namespace Application.Features.Routing.Models;

public sealed record PlanningSettingsState(
  PlanningPreferences Preferences,
  long Revision,
  DateTime? UpdatedAt
);
