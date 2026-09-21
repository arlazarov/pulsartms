namespace Domain.Models.Routing;

public sealed record PlanningSettingsUpdate(
  PlanningPreferences Preferences,
  long Revision
);
