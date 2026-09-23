using System.Globalization;
using System.Text.Json;
using Domain.Models.Messaging;

namespace Infrastructure.Integrations.WhatsApp;

// The parts of a WhatsApp Business webhook PulsR uses: message statuses by
// message id, and the messages drivers write - id, time, kind, text or
// caption, and a file's media id, type, hash and name. Profile names and
// other contact data are not read. Changes addressed to another business
// number are dropped here, whatever the signature said.
public static class WhatsAppNotifications
{
  private const int MaximumEvents = 500;
  public const int MaximumText = 4096;
  public const int MaximumCaption = 1024;

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
            inbound.Add(Inbound(message, from, at));
      }
    }
    catch (JsonException)
    {
      return new([], [], 0);
    }
    return new(statuses, inbound, others) { BusinessNumberId = phoneNumberId };
  }

  private static DriverMessageInboundEvent Inbound(
    JsonElement message,
    string from,
    DateTime at
  )
  {
    var id = Text(message, "id") is { Length: > 0 and <= 200 } value
      ? value
      : null;
    var type = Text(message, "type") ?? "";
    if (type == "text")
      return new(from, at)
      {
        ProviderMessageId = id,
        Text = Bounded(Child(message, "text", "body"), MaximumText),
      };
    if (
      type is "image" or "document" or "audio" or "video"
      && message.TryGetProperty(type, out var media)
      && media.ValueKind == JsonValueKind.Object
      && Text(media, "id") is { } mediaId
      && MediaId(mediaId)
    )
      return new(from, at)
      {
        ProviderMessageId = id,
        Kind = "file",
        Text = Bounded(Text(media, "caption"), MaximumCaption),
        Media = new(
          mediaId,
          Bounded(Text(media, "mime_type"), 100),
          Text(media, "sha256") is { Length: <= 100 } sha ? sha : null,
          Text(media, "filename") is { } name ? Bounded(name, 200) : null
        ),
      };
    return new(from, at)
    {
      ProviderMessageId = id,
      Kind = "unsupported",
      Text = Bounded(type, 40),
    };
  }

  private static string? Child(JsonElement parent, string name, string field) =>
    parent.TryGetProperty(name, out var child)
    && child.ValueKind == JsonValueKind.Object
      ? Text(child, field)
      : null;

  private static string Bounded(string? text, int length) =>
    text is null ? ""
    : text.Length <= length ? text
    : text[..length];

  private static bool MediaId(string id) =>
    id.Length is > 0 and <= 200
    && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

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
