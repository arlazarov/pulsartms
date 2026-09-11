using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Integrations.Samsara.Models;

public sealed class OptionalSamsaraTimeConverter : JsonConverter<DateTime?>
{
  public override DateTime? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null) return null;
    if (reader.TokenType == JsonTokenType.String && string.IsNullOrWhiteSpace(reader.GetString())) return null;
    return reader.GetDateTime();
  }

  public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
  {
    if (value.HasValue) writer.WriteStringValue(value.Value);
    else writer.WriteNullValue();
  }
}
