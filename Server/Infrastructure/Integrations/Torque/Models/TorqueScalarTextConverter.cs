using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Integrations.Torque.Models;

public sealed class TorqueScalarTextConverter : JsonConverter<string>
{
  public override bool HandleNull => true;

  public override string? Read(
    ref Utf8JsonReader reader,
    Type type,
    JsonSerializerOptions options
  ) =>
    reader.TokenType switch
    {
      JsonTokenType.String => reader.GetString(),
      JsonTokenType.Number => reader
        .GetDecimal()
        .ToString(CultureInfo.InvariantCulture),
      JsonTokenType.Null => "",
      _ => throw new JsonException("Unsupported Torque text value."),
    };

  public override void Write(
    Utf8JsonWriter writer,
    string value,
    JsonSerializerOptions options
  ) => writer.WriteStringValue(value);
}
