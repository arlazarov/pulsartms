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

  private static string Duration(int minutes) =>
    $"{minutes / 60}h {minutes % 60:00}m";

  private static string StatusDuration(int minutes) =>
    minutes < 60 ? $"{minutes} min" : $"{minutes / 60}h {minutes % 60}min";
}
