namespace Client.Models.DTO.Planning;

public sealed record PlanningSettingsState(
  PlanningPreferences Preferences,
  long Revision,
  DateTime? UpdatedAt
);
