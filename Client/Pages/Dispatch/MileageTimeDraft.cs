using System.Globalization;

namespace Client.Pages.Dispatch;

public sealed class MileageTimeDraft
{
  public DateTime? Date { get; set; }
  public string Time { get; set; } = "";
  public bool Empty => Date is null && string.IsNullOrWhiteSpace(Time);

  public bool TryRead(out DateTimeOffset? value)
  {
    value = null;
    if (Empty)
      return true;
    if (
      Date is not { } date
      || !TimeOnly.TryParseExact(
        Time.Trim(),
        ["hh:mm tt", "hh:mm:ss tt"],
        CultureInfo.InvariantCulture,
        DateTimeStyles.AllowWhiteSpaces,
        out var time
      )
    )
      return false;
    var local = DateTime.SpecifyKind(
      date.Date + time.ToTimeSpan(),
      DateTimeKind.Unspecified
    );
    if (
      TimeZoneInfo.Local.IsInvalidTime(local)
      || TimeZoneInfo.Local.IsAmbiguousTime(local)
    )
      return false;
    value = new(local, TimeZoneInfo.Local.GetUtcOffset(local));
    return true;
  }

  public static MileageTimeDraft From(DateTime? utc)
  {
    if (utc is null)
      return new();
    var local = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime();
    return new()
    {
      Date = local.Date,
      Time = local.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture),
    };
  }
}
