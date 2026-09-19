using Client.Models.DTO.Planning;

namespace Client.Shared.Dispatch;

internal static class DispatchCycleDisplay
{
  public static string Remaining(StopCycleForecast? forecast) =>
    forecast is { RemainingMinutes: >= 0 }
      ? $"~{Duration(forecast.RemainingMinutes)}"
      : "—";

  public static string Duration(int minutes) =>
    $"{minutes / 60}h {minutes % 60:00}m";
}
