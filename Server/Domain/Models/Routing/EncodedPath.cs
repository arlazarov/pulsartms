using System.Text;

namespace Domain.Models.Routing;

// Route geometry as one string instead of an object per point. A point written
// as {"latitude":…,"longitude":…} costs about forty-five bytes and, in the
// browser, an allocation; the same point here costs about six bytes and none.
// A long route is tens of thousands of points, and it crosses two
// serialization boundaries on its way to the map.
//
// This is the common encoded-polyline scheme at six decimal places - about
// eleven centimetres, well inside the two metres display geometry is already
// simplified to, so route progress lands where it did before.
public static class EncodedPath
{
  private const double Scale = 1e6;

  public static string Encode(IReadOnlyList<RoutePoint> points)
  {
    var text = new StringBuilder(points.Count * 6);
    long latitude = 0,
      longitude = 0;
    foreach (var point in points)
    {
      var nextLatitude = (long)Math.Round(point.Latitude * Scale);
      var nextLongitude = (long)Math.Round(point.Longitude * Scale);
      Append(text, nextLatitude - latitude);
      Append(text, nextLongitude - longitude);
      latitude = nextLatitude;
      longitude = nextLongitude;
    }
    return text.ToString();
  }

  public static List<RoutePoint> Decode(string? text)
  {
    List<RoutePoint> points = [];
    if (string.IsNullOrEmpty(text))
      return points;
    var index = 0;
    long latitude = 0,
      longitude = 0;
    while (index < text.Length)
    {
      if (
        !TryRead(text, ref index, out var latitudeStep)
        || !TryRead(text, ref index, out var longitudeStep)
      )
        throw new FormatException("The encoded path is cut short.");
      latitude += latitudeStep;
      longitude += longitudeStep;
      points.Add(new(latitude / Scale, longitude / Scale));
    }
    return points;
  }

  private static void Append(StringBuilder text, long value)
  {
    var bits = value < 0 ? ~(value << 1) : value << 1;
    while (bits >= 0x20)
    {
      text.Append((char)((0x20 | (bits & 0x1f)) + 63));
      bits >>= 5;
    }
    text.Append((char)(bits + 63));
  }

  private static bool TryRead(string text, ref int index, out long value)
  {
    long bits = 0;
    var shift = 0;
    value = 0;
    while (index < text.Length)
    {
      var chunk = text[index++] - 63;
      if (chunk is < 0 or > 0x3f || shift > 60)
        throw new FormatException("The encoded path is not valid.");
      bits |= (long)(chunk & 0x1f) << shift;
      shift += 5;
      if (chunk < 0x20)
      {
        value = (bits & 1) == 1 ? ~(bits >> 1) : bits >> 1;
        return true;
      }
    }
    return false;
  }
}
