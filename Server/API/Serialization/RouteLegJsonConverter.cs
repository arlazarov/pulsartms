using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Features.Routing.Models;

namespace API.Serialization;

// Route geometry leaves the API as an encoded string when the caller has said
// it can read one, and as the list of points it always was when it has not.
//
// It is decided here, at the edge, so that nothing inside the application
// knows there are two forms. It is decided per request because the client and
// the server are deployed separately: a tab opened before a release keeps
// running the old client for hours, and must keep drawing routes.
public sealed class RouteLegJsonConverter(IHttpContextAccessor http)
  : JsonConverter<RouteLeg>
{
  public const string Header = "X-Route-Geometry";
  public const string Encoded = "encoded";

  public override RouteLeg Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options
  )
  {
    if (reader.TokenType != JsonTokenType.StartObject)
      throw new JsonException("A route leg must be an object.");
    double miles = 0,
      seconds = 0;
    List<RoutePoint>? points = null;
    string? path = null;
    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
    {
      var name = reader.GetString();
      reader.Read();
      if (Is(name, nameof(RouteLeg.Miles)))
        miles = reader.GetDouble();
      else if (Is(name, nameof(RouteLeg.Seconds)))
        seconds = reader.GetDouble();
      else if (Is(name, nameof(RouteLeg.Points)))
        points = JsonSerializer.Deserialize<List<RoutePoint>>(
          ref reader,
          options
        );
      else if (Is(name, "Path"))
        path = reader.GetString();
      else
        reader.Skip();
    }
    if (points is not { Count: > 0 })
      points = ReadPath(path) ?? points;
    return new(miles, seconds, points ?? []);
  }

  public override void Write(
    Utf8JsonWriter writer,
    RouteLeg value,
    JsonSerializerOptions options
  )
  {
    writer.WriteStartObject();
    writer.WriteNumber(Name(options, nameof(RouteLeg.Miles)), value.Miles);
    writer.WriteNumber(Name(options, nameof(RouteLeg.Seconds)), value.Seconds);
    WriteGeometry(writer, value.Points, options, http);
    writer.WriteEndObject();
  }

  // `points`, and beside it `path` when the caller asked for the encoded
  // form. A leg without geometry is never given an empty path: the reader
  // takes an empty `points` with a `path` to mean "decode this".
  internal static void WriteGeometry(
    Utf8JsonWriter writer,
    IReadOnlyList<RoutePoint> points,
    JsonSerializerOptions options,
    IHttpContextAccessor http
  )
  {
    var encoded =
      points.Count > 0
      && string.Equals(
        http.HttpContext?.Request.Headers[Header],
        Encoded,
        StringComparison.OrdinalIgnoreCase
      );
    writer.WritePropertyName(Name(options, nameof(RouteLeg.Points)));
    if (!encoded)
    {
      JsonSerializer.Serialize(writer, points, options);
      return;
    }
    writer.WriteStartArray();
    writer.WriteEndArray();
    // The alphabet is printable ASCII; the default encoder would spend six
    // bytes on each backtick for the sake of HTML this never goes into.
    writer.WriteString(
      Name(options, "Path"),
      JsonEncodedText.Encode(
        EncodedPath.Encode(points),
        JavaScriptEncoder.UnsafeRelaxedJsonEscaping
      )
    );
  }

  internal static List<RoutePoint>? ReadPath(string? path)
  {
    if (path is not { Length: > 0 })
      return null;
    try
    {
      return EncodedPath.Decode(path);
    }
    catch (FormatException error)
    {
      throw new JsonException(error.Message, error);
    }
  }

  internal static bool Is(string? actual, string expected) =>
    string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

  internal static string Name(JsonSerializerOptions options, string name) =>
    options.PropertyNamingPolicy?.ConvertName(name) ?? name;
}
