namespace Client.Models.DTO.Planning;

public sealed record PlanningSettingsUpdate(
  PlanningPreferences Preferences,
  long Revision
);
