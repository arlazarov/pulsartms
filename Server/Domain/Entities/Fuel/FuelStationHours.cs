using System.Text.Json;

namespace Domain.Entities.Fuel;

// Whether a station is open at the moment a driver would reach it.
//
// A fuel stop is chosen hours ahead, so "open now" is the wrong question;
// the question is whether it is open on arrival. The provider states its
// weekly periods in the station's own local time, which is why the offset
// is stored beside them - a stop in Arizona and one in Ontario do not share
// a clock.
//
// Every uncertain answer is Unknown rather than Closed. A station nobody has
// hours for must not be refused: most of the fleet's stops are truck stops
// that never close, and refusing the unknown would leave a driver with no
// fuel stop at all, which is worse than any of this.
public static class FuelStationHours
{
  public enum Openness
  {
    Unknown,
    Open,
    Closed,
  }

  public static Openness At(
    string? periodsJson,
    int? utcOffsetMinutes,
    DateTime utc
  )
  {
    if (string.IsNullOrWhiteSpace(periodsJson) || utcOffsetMinutes is null)
      return Openness.Unknown;
    List<(int Day, int Minute, bool Opens)> edges;
    bool stated;
    try
    {
      (edges, stated) = Edges(periodsJson);
    }
    catch (JsonException)
    {
      return Openness.Unknown;
    }
    if (edges.Count == 0)
      // An empty period list is how the provider states a place that never
      // closes. A list that held entries none of which could be read is a
      // different answer: it stated something and this could not follow it,
      // which is not permission to call the station open.
      return stated ? Openness.Unknown : Openness.Open;

    var local = utc.AddMinutes(utcOffsetMinutes.Value);
    var minute = (int)local.DayOfWeek * 1440 + local.Hour * 60 + local.Minute;
    // The last edge at or before this minute decides it, wrapping around the
    // week so a period that runs past midnight into Monday still covers
    // Sunday night.
    var week = 7 * 1440;
    var best = -1;
    var open = false;
    foreach (var edge in edges)
    {
      var at = edge.Day * 1440 + edge.Minute;
      var distance = (minute - at + week) % week;
      if (best < 0 || distance < best)
      {
        best = distance;
        open = edge.Opens;
      }
    }
    return open ? Openness.Open : Openness.Closed;
  }

  private static (
    List<(int Day, int Minute, bool Opens)> Edges,
    bool Stated
  ) Edges(string json)
  {
    var edges = new List<(int, int, bool)>();
    using var document = JsonDocument.Parse(json);
    if (!document.RootElement.TryGetProperty("periods", out var periods))
      return (edges, true);
    var stated = false;
    foreach (var period in periods.EnumerateArray())
    {
      stated = true;
      Add(period, "open", true);
      Add(period, "close", false);
      continue;
      void Add(JsonElement source, string name, bool opens)
      {
        if (
          !source.TryGetProperty(name, out var point)
          || !point.TryGetProperty("day", out var day)
          || !day.TryGetInt32(out var dayValue)
          || dayValue is < 0 or > 6
        )
          return;
        var hour =
          point.TryGetProperty("hour", out var h) && h.TryGetInt32(out var hv)
            ? hv
            : 0;
        var minute =
          point.TryGetProperty("minute", out var m) && m.TryGetInt32(out var mv)
            ? mv
            : 0;
        edges.Add((dayValue, hour * 60 + minute, opens));
      }
    }
    return (edges, stated);
  }
}
