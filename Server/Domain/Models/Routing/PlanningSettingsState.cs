namespace Domain.Models.Routing;

public sealed record PlanningSettingsState(
  PlanningPreferences Preferences,
  long Revision,
  DateTime? UpdatedAt
);
