using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Models.Routing;

namespace Domain.Rules;

// A saved road holds tens of thousands of points. Read through the record's
// constructor, System.Text.Json builds an argument object and state per
// point, most of what reading a road allocates. This reads and writes the
// same {"latitude":…,"longitude":…} object the default did, byte for byte,
// with the web defaults it replaces: names in any case, numbers also as
// strings, other properties skipped, a missing one zero.
public sealed class RoutePointJson : JsonConverter<RoutePoint>
{
  public override RoutePoint Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options
  )
  {
    if (reader.TokenType != JsonTokenType.StartObject)
      throw new JsonException("A route point is an object.");
    double latitude = 0,
      longitude = 0;
    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
    {
      if (reader.TokenType != JsonTokenType.PropertyName)
        throw new JsonException("A route point holds properties.");
      var name = Name(ref reader);
      reader.Read();
      if (name == 1)
        latitude = Number(ref reader);
      else if (name == 2)
        longitude = Number(ref reader);
      else
        reader.Skip();
    }
    return new(latitude, longitude);
  }

  public override void Write(
    Utf8JsonWriter writer,
    RoutePoint value,
    JsonSerializerOptions options
  )
  {
    writer.WriteStartObject();
    writer.WriteNumber("latitude", value.Latitude);
    writer.WriteNumber("longitude", value.Longitude);
    writer.WriteEndObject();
  }

  // 1 latitude, 2 longitude, 0 anything else; only an unusual casing
  // allocates the name.
  private static int Name(ref Utf8JsonReader reader)
  {
    if (reader.ValueTextEquals("latitude"u8))
      return 1;
    if (reader.ValueTextEquals("longitude"u8))
      return 2;
    var name = reader.GetString();
    return string.Equals(name, "latitude", StringComparison.OrdinalIgnoreCase)
        ? 1
      : string.Equals(name, "longitude", StringComparison.OrdinalIgnoreCase) ? 2
      : 0;
  }

  private static double Number(ref Utf8JsonReader reader) =>
    reader.TokenType switch
    {
      JsonTokenType.Number => reader.GetDouble(),
      JsonTokenType.String
        when double.TryParse(
          reader.GetString(),
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out var value
        ) && double.IsFinite(value) => value,
      _ => throw new JsonException("A coordinate is a number."),
    };
}
