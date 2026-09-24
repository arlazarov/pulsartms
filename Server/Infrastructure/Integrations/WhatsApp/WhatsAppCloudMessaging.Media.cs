using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.Features.Messaging.Interfaces;
using Domain.Models.Messaging;
using Infrastructure.Storage;

namespace Infrastructure.Integrations.WhatsApp;

// Files in both directions: a driver's file streamed from the provider's
// short-lived address, and a dispatcher's file uploaded to the provider and
// then sent by its media id.
public sealed partial class WhatsAppCloudMessaging
{
  public async Task<DriverMedia?> OpenMediaAsync(
    string mediaId,
    CancellationToken ct
  )
  {
    if (await SettingsAsync(ct) is not { } settings)
      throw new DriverMessagingUnavailableException("Not configured.");
    if (!MediaIdPattern().IsMatch(mediaId))
      return null;
    using var describe = Authorized(
      HttpMethod.Get,
      $"https://graph.facebook.com/{Version()}/{mediaId}"
        + $"?phone_number_id={settings.PhoneNumberId}",
      settings
    );
    string body;
    using (var described = await SendAsync(describe, ct))
    {
      if (
        described.StatusCode
        is HttpStatusCode.NotFound
          or HttpStatusCode.BadRequest
      )
        return null;
      if (!described.IsSuccessStatusCode)
        throw new DriverMessagingUnavailableException("Media not described.");
      body = await described.Content.ReadAsStringAsync(ct);
    }
    var media = Media(body);
    if (media is null)
      throw new DriverMessagingUnavailableException("Media not described.");
    // The address lasts minutes and needs the token; it is used at once and
    // never stored or given to a browser.
    using var fetch = Authorized(
      HttpMethod.Get,
      media.Url.ToString(),
      settings
    );
    var response = await SendAsync(
      fetch,
      ct,
      HttpCompletionOption.ResponseHeadersRead
    );
    if (response.StatusCode == HttpStatusCode.NotFound)
    {
      response.Dispose();
      return null;
    }
    if (!response.IsSuccessStatusCode)
    {
      response.Dispose();
      throw new DriverMessagingUnavailableException("Media not downloaded.");
    }
    return new(
      new ResponseStream(
        response,
        await response.Content.ReadAsStreamAsync(ct)
      ),
      media.Length,
      media.MimeType,
      media.Sha256
    );
  }

  private sealed record MediaDescription(
    Uri Url,
    long Length,
    string MimeType,
    string Sha256
  );

  // Only an https address on Meta's media hosts is fetched with the token.
  private static MediaDescription? Media(string body)
  {
    try
    {
      using var document = JsonDocument.Parse(body);
      var root = document.RootElement;
      return
        root.TryGetProperty("url", out var url)
        && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var address)
        && address.Scheme == Uri.UriSchemeHttps
        && (
          address.Host.EndsWith(".fbsbx.com", StringComparison.Ordinal)
          || address.Host.EndsWith(".facebook.com", StringComparison.Ordinal)
        )
        && root.TryGetProperty("file_size", out var size)
        && size.TryGetInt64(out var length)
        && length > 0
        && root.TryGetProperty("mime_type", out var type)
        && type.GetString() is { Length: > 0 and <= 100 } mime
        && root.TryGetProperty("sha256", out var sha)
        && Hex(sha.GetString()) is { } hex
        ? new(address, length, mime, hex)
        : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  // The provider's hash as lower-case hex, whether it came as hex or base64.
  public static string? Hex(string? value)
  {
    if (value is { Length: 64 } && value.All(char.IsAsciiHexDigit))
      return value.ToLowerInvariant();
    try
    {
      var bytes = value is null ? [] : Convert.FromBase64String(value);
      return bytes.Length == 32 ? Convert.ToHexStringLower(bytes) : null;
    }
    catch (FormatException)
    {
      return null;
    }
  }

  public async Task<DriverMessageSendResult> SendFileAsync(
    string businessNumber,
    string recipient,
    DriverFile file,
    CancellationToken ct
  )
  {
    if (await SettingsAsync(ct) is not { } settings)
      return new(DriverMessageOutcome.NotConfigured);
    if (settings.PhoneNumberId != businessNumber)
      return new(DriverMessageOutcome.NumberChanged);
    // Nothing reaches the driver until the send below, so an upload that
    // fails or goes unanswered is simply not sent.
    string? mediaId;
    try
    {
      mediaId = await UploadAsync(settings, file, ct);
    }
    catch (DriverMessagingUnavailableException)
    {
      mediaId = null;
    }
    if (mediaId is null)
      return new(DriverMessageOutcome.Rejected);
    var kind = file.ContentType.Split('/')[0] switch
    {
      "image" => "image",
      "audio" => "audio",
      "video" => "video",
      _ => "document",
    };
    object media = kind switch
    {
      "audio" => new { id = mediaId },
      "document" => new
      {
        id = mediaId,
        caption = file.Caption,
        filename = file.FileName,
      },
      _ => new { id = mediaId, caption = file.Caption },
    };
    var payload = new Dictionary<string, object>
    {
      ["messaging_product"] = "whatsapp",
      ["recipient_type"] = "individual",
      ["to"] = recipient,
      ["type"] = kind,
      [kind] = media,
    };
    return await PostMessageAsync(settings, payload, ct);
  }

  private async Task<string?> UploadAsync(
    Settings settings,
    DriverFile file,
    CancellationToken ct
  )
  {
    using var request = Authorized(
      HttpMethod.Post,
      $"https://graph.facebook.com/{Version()}/{settings.PhoneNumberId}/media",
      settings
    );
    var content = new StreamContent(file.Content);
    content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
    content.Headers.ContentLength = file.Length;
    request.Content = new MultipartFormDataContent
    {
      { new StringContent("whatsapp"), "messaging_product" },
      { new StringContent(file.ContentType), "type" },
      { content, "file", file.FileName },
    };
    using var response = await SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
      return null;
    try
    {
      using var document = JsonDocument.Parse(
        await response.Content.ReadAsStringAsync(ct)
      );
      return
        document.RootElement.TryGetProperty("id", out var id)
        && id.GetString() is { } value
        && MediaIdPattern().IsMatch(value)
        ? value
        : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }
}
