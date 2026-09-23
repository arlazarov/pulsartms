using System.Globalization;
using System.Text.Json;
using Domain.Models.Messaging;

namespace Infrastructure.Integrations.WhatsApp;

// The parts of a WhatsApp Business webhook PulsR uses: message statuses by
// message id, and when a number last wrote in. Message contents, names and
// profile data are not read. Changes addressed to another business number
// are dropped here, whatever the signature said.
public static class WhatsAppNotifications
{
  private const int MaximumEvents = 500;

  public static DriverMessagingNotification Read(
    ReadOnlyMemory<byte> body,
    string phoneNumberId
  )
  {
    var statuses = new List<DriverMessageStatusEvent>();
    var inbound = new List<DriverMessageInboundEvent>();
    var others = 0;
    try
    {
      using var document = JsonDocument.Parse(
        body,
        new JsonDocumentOptions { MaxDepth = 16 }
      );
      var root = document.RootElement;
      if (
        root.ValueKind != JsonValueKind.Object
        || Text(root, "object") != "whatsapp_business_account"
      )
        return new([], [], 0);
      foreach (var entry in Items(root, "entry"))
      foreach (var change in Items(entry, "changes"))
      {
        if (
          Text(change, "field") != "messages"
          || !change.TryGetProperty("value", out var value)
          || value.ValueKind != JsonValueKind.Object
        )
          continue;
        var metadata = value.TryGetProperty("metadata", out var m)
          ? m
          : default;
        if (
          metadata.ValueKind != JsonValueKind.Object
          || Text(metadata, "phone_number_id") != phoneNumberId
        )
        {
          others++;
          continue;
        }
        foreach (var status in Items(value, "statuses"))
          if (
            statuses.Count < MaximumEvents
            && Text(status, "id") is { Length: > 0 and <= 200 } id
            && Status(Text(status, "status")) is { } state
            && Time(status) is { } at
          )
            statuses.Add(new(id, state, at, Error(status)));
        foreach (var message in Items(value, "messages"))
          if (
            inbound.Count < MaximumEvents
            && Phone(Text(message, "from")) is { } from
            && Time(message) is { } at
          )
            inbound.Add(new(from, at));
      }
    }
    catch (JsonException)
    {
      return new([], [], 0);
    }
    return new(statuses, inbound, others);
  }

  private static IEnumerable<JsonElement> Items(JsonElement parent, string name)
  {
    if (
      parent.ValueKind != JsonValueKind.Object
      || !parent.TryGetProperty(name, out var items)
      || items.ValueKind != JsonValueKind.Array
    )
      yield break;
    foreach (var item in items.EnumerateArray())
      if (item.ValueKind == JsonValueKind.Object)
        yield return item;
  }

  private static string? Text(JsonElement parent, string name) =>
    parent.TryGetProperty(name, out var value)
    && value.ValueKind == JsonValueKind.String
      ? value.GetString()
      : null;

  private static string? Status(string? status) =>
    status switch
    {
      "sent" => DriverMessageStatuses.Sent,
      "delivered" => DriverMessageStatuses.Delivered,
      "read" => DriverMessageStatuses.Read,
      "failed" => DriverMessageStatuses.Failed,
      _ => null,
    };

  private static DateTime? Time(JsonElement parent) =>
    long.TryParse(
      Text(parent, "timestamp"),
      NumberStyles.None,
      CultureInfo.InvariantCulture,
      out var seconds
    ) && seconds is > 0 and < 32_503_680_000
      ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
      : null;

  private static int? Error(JsonElement status)
  {
    foreach (var error in Items(status, "errors"))
      if (
        error.TryGetProperty("code", out var code)
        && code.TryGetInt32(out var value)
      )
        return value;
    return null;
  }

  // WhatsApp ids are the number's digits without "+".
  private static string? Phone(string? waId) =>
    waId is { Length: >= 8 and <= 15 }
    && waId[0] != '0'
    && waId.All(char.IsAsciiDigit)
      ? "+" + waId
      : null;
}
