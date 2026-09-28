using Client.Models.DTO.Planning;

namespace Client.Services;

// The short name read beside a stop's local time. North American zones go
// by the letters dispatchers use (EDT, CST, MST without daylight time in
// Arizona); anywhere else the time reads with its UTC offset. An unknown
// zone reads as nothing rather than as a guess.
public static class StopTimeZoneLabel
{
  private static readonly Dictionary<TimeSpan, string> Regions = new()
  {
    [TimeSpan.FromHours(-3.5)] = "N",
    [TimeSpan.FromHours(-4)] = "A",
    [TimeSpan.FromHours(-5)] = "E",
    [TimeSpan.FromHours(-6)] = "C",
    [TimeSpan.FromHours(-7)] = "M",
    [TimeSpan.FromHours(-8)] = "P",
    [TimeSpan.FromHours(-9)] = "AK",
    [TimeSpan.FromHours(-10)] = "H",
  };

  // The zone a stop's booking is written in: the forecast for that same
  // stop knows the stop's zone; failing that, the source's own. None
  // known, none said.
  public static string? ForAppointment(PlanStop stop, string? forecastZoneId) =>
    stop.ScheduledDate is { } date
      ? For(
        string.IsNullOrWhiteSpace(forecastZoneId)
          ? stop.AppointmentTimeZoneId
          : forecastZoneId,
        date,
        stop.ScheduledTime
      )
      : null;

  public static string? For(string? zoneId, DateOnly date, TimeOnly? time)
  {
    if (string.IsNullOrWhiteSpace(zoneId))
      return null;
    TimeZoneInfo zone;
    try
    {
      zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
    }
    catch (Exception error)
      when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
    {
      return null;
    }
    var local = date.ToDateTime(time ?? new TimeOnly(12, 0));
    if (
      (
        zoneId.StartsWith("America/", StringComparison.Ordinal)
        || zoneId == "Pacific/Honolulu"
      ) && Regions.TryGetValue(zone.BaseUtcOffset, out var region)
    )
      return region + (zone.IsDaylightSavingTime(local) ? "DT" : "ST");
    var offset = zone.GetUtcOffset(local);
    return $"UTC{(offset < TimeSpan.Zero ? "−" : "+")}{offset:hh\\:mm}";
  }
}
