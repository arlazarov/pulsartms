using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Features.Routing.Models;

namespace API.Serialization;

// The empty drive between two loads is the other place route geometry leaves
// the API. It is not a leg, so it needs its own converter, but the two forms
// and the rule for choosing between them are the leg's.
public sealed class NextLoadConnectionJsonConverter(IHttpContextAccessor http)
  : JsonConverter<NextLoadConnection>
{
  public override NextLoadConnection Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options
  )
  {
    if (reader.TokenType != JsonTokenType.StartObject)
      throw new JsonException("A connection must be an object.");
    double miles = 0;
    List<RoutePoint>? points = null;
    string? path = null;
    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
    {
      var name = reader.GetString();
      reader.Read();
      if (RouteLegJsonConverter.Is(name, nameof(NextLoadConnection.Miles)))
        miles = reader.GetDouble();
      else if (
        RouteLegJsonConverter.Is(name, nameof(NextLoadConnection.Points))
      )
        points = JsonSerializer.Deserialize<List<RoutePoint>>(
          ref reader,
          options
        );
      else if (RouteLegJsonConverter.Is(name, "Path"))
        path = reader.GetString();
      else
        reader.Skip();
    }
    if (points is not { Count: > 0 })
      points = RouteLegJsonConverter.ReadPath(path) ?? points;
    return new(miles, points ?? []);
  }

  public override void Write(
    Utf8JsonWriter writer,
    NextLoadConnection value,
    JsonSerializerOptions options
  )
  {
    writer.WriteStartObject();
    writer.WriteNumber(
      RouteLegJsonConverter.Name(options, nameof(NextLoadConnection.Miles)),
      value.Miles
    );
    RouteLegJsonConverter.WriteGeometry(writer, value.Points, options, http);
    writer.WriteEndObject();
  }
}
