using Client.Models.DTO.Planning;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.DriverStatus.DriverDutySummary;

public partial class DriverDutySummary
{
  [Parameter]
  public DriverDutyStatus? Status { get; set; }

  [Parameter]
  public string? CurrentStatus { get; set; }

  [Parameter]
  public DateTime? ClockUpdatedAt { get; set; }

  [Parameter]
  public bool Compact { get; set; }

  [Parameter]
  public bool ShowRestDetails { get; set; } = true;

  // "row": the map card's single line of status, rest and reset.
  [Parameter]
  public string Reading { get; set; } = string.Empty;
  private string? ShownStatus =>
    string.IsNullOrEmpty(CurrentStatus) ? Status?.Status : CurrentStatus;
  private string StatusIcon =>
    ShownStatus switch
    {
      "offDuty" or "sleeperBerth" => "moon",
      "driving" or "yardMove" or "personalConveyance" => "truck",
      _ => "clock",
    };
  private string? ResetRemaining =>
    Status
      is {
        CycleResetHours: > 0 and var hours,
        CycleResetRemainingMinutes: >= 0 and var remaining,
        CycleResetCountry: "US" or "CA"
      }
      ? remaining > 0
        ? $"{hours}h reset in {Duration(remaining)}"
        : $"{hours}h reset done"
      : null;
  private bool Matches =>
    string.IsNullOrEmpty(CurrentStatus) || CurrentStatus == Status?.Status;
  private string? CycleResetText =>
    Status
      is {
        CycleResetHours: > 0,
        CycleResetRemainingMinutes: >= 0,
        CycleResetCountry: "US" or "CA"
      }
      ? Status.CycleResetRemainingMinutes > 0
        ? $"{Duration(Status.CycleResetRemainingMinutes.Value)} left to complete {Status.CycleResetHours}h reset · {Status.CycleResetCountry}"
        : $"{Status.CycleResetHours}h reset reached · {Status.CycleResetCountry}"
      : null;
  private bool Fresh =>
    (
      ClockUpdatedAt is { } updated
        ? new DateTimeOffset(DateTime.SpecifyKind(updated, DateTimeKind.Utc))
        : Status?.ObservedAt
    ) >= DateTimeOffset.UtcNow.AddMinutes(-3)
    && (
      Status is null
      || Status.ObservedAt >= DateTimeOffset.UtcNow.AddMinutes(-3)
    );

  // The status as a word, for a holder that shows it on its own.
  public static string StatusName(string? value) => Label(value);

  private static string Label(string? value) =>
    value switch
    {
      "driving" => "Driving",
      "onDuty" => "On Duty",
      "yardMove" => "Yard Move",
      "offDuty" => "Off Duty",
      "sleeperBerth" => "Sleeper Berth",
      "personalConveyance" => "Personal Conveyance (PC)",
      _ => "—",
    };

  private const string RestTitle =
    "Rest built up since the last work, towards a 10-hour daily rest";
  private const string ResetTitle =
    "The same rest, towards the cycle reset of the ruleset the hours are read under";

  private static string ShortLabel(string? value) =>
    value switch
    {
      "driving" => "Driving",
      "onDuty" => "On duty",
      "yardMove" => "Yard move",
      "offDuty" => "Off duty",
      "sleeperBerth" => "Sleeper",
      "personalConveyance" => "PC",
      _ => "Duty —",
    };

  public static string Duration(int minutes) =>
    $"{minutes / 60}h {minutes % 60:00}m";

  private static string StatusDuration(int minutes) =>
    minutes < 60 ? $"{minutes} min" : $"{minutes / 60}h {minutes % 60}min";
}
