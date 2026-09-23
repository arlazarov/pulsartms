using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Application.Features.Routing.Interfaces;
using Domain.Models.Messaging;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.WhatsApp;

// WhatsApp Cloud API for the serving company, with the credentials its
// administrator saved in Settings. One request per send and never a retry:
// a timeout, a dropped connection or a server error after the request left
// is reported as unknown, because the message may already be on its way.
// Provider error text can quote the recipient, so only the numeric code is
// returned.
public sealed partial class WhatsAppCloudMessaging(
  HttpClient http,
  IIntegrationCredentials credentials,
  IConfiguration configuration
) : IDriverMessaging
{
  // The Graph API version requests are pinned to; each version is served
  // for about two years. WhatsApp:GraphApiVersion overrides it.
  public const string DefaultGraphVersion = "v26.0";
  public const int MaximumText = 4096;

  public string Channel => DriverMessageChannels.WhatsApp;

  public async Task<bool> IsConfiguredAsync(CancellationToken ct) =>
    await SettingsAsync(ct) is not null;

  public async Task<DriverMessageSendResult> SendTextAsync(
    string recipient,
    string text,
    CancellationToken ct
  )
  {
    if (await SettingsAsync(ct) is not { } settings)
      return new(DriverMessageOutcome.NotConfigured);
    if (text.Length is 0 or > MaximumText)
      throw new ArgumentException("The message length is not allowed.");
    using var request = new HttpRequestMessage(
      HttpMethod.Post,
      $"https://graph.facebook.com/{Version()}/{settings.PhoneNumberId}/messages"
    )
    {
      Content = JsonContent.Create(
        new
        {
          messaging_product = "whatsapp",
          recipient_type = "individual",
          to = recipient,
          type = "text",
          text = new { preview_url = false, body = text },
        }
      ),
    };
    request.Headers.Authorization = new AuthenticationHeaderValue(
      "Bearer",
      settings.AccessToken
    );
    HttpResponseMessage response;
    try
    {
      response = await http.SendAsync(request, ct);
    }
    catch (Exception exception)
      when (exception is HttpRequestException or OperationCanceledException)
    {
      return new(DriverMessageOutcome.Unknown);
    }
    using (response)
    {
      string body;
      try
      {
        body = await response.Content.ReadAsStringAsync(ct);
      }
      catch (Exception exception)
        when (exception is HttpRequestException or OperationCanceledException)
      {
        return new(DriverMessageOutcome.Unknown);
      }
      if ((int)response.StatusCode >= 500)
        return new(DriverMessageOutcome.Unknown, ErrorCode: ErrorCode(body));
      if (!response.IsSuccessStatusCode)
        return new(DriverMessageOutcome.Rejected, ErrorCode: ErrorCode(body));
      return MessageId(body) is { } id
        ? new(DriverMessageOutcome.Accepted, id)
        : new(DriverMessageOutcome.Unknown);
    }
  }

  public async Task<bool> AcceptsSubscriptionAsync(
    string? verifyToken,
    CancellationToken ct
  ) =>
    !string.IsNullOrEmpty(verifyToken)
    && await SettingsAsync(ct) is { } settings
    && CryptographicOperations.FixedTimeEquals(
      Encoding.UTF8.GetBytes(verifyToken),
      Encoding.UTF8.GetBytes(settings.VerifyToken)
    );

  public async Task<DriverMessagingNotification?> ReadNotificationAsync(
    ReadOnlyMemory<byte> body,
    string? signature,
    CancellationToken ct
  ) =>
    await SettingsAsync(ct) is { } settings
    && Signed(body.Span, signature, settings.AppSecret)
      ? WhatsAppNotifications.Read(body, settings.PhoneNumberId)
      : null;

  public static bool Signed(
    ReadOnlySpan<byte> body,
    string? signature,
    string appSecret
  )
  {
    const string prefix = "sha256=";
    if (
      signature is null
      || !signature.StartsWith(prefix, StringComparison.Ordinal)
      || signature.Length != prefix.Length + 64
    )
      return false;
    byte[] claimed;
    try
    {
      claimed = Convert.FromHexString(signature.AsSpan(prefix.Length));
    }
    catch (FormatException)
    {
      return false;
    }
    var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body);
    return CryptographicOperations.FixedTimeEquals(claimed, expected);
  }

  private sealed record Settings(
    string PhoneNumberId,
    string AccessToken,
    string AppSecret,
    string VerifyToken
  );

  private async Task<Settings?> SettingsAsync(CancellationToken ct)
  {
    var values = await credentials.GetAsync(
      IntegrationProviderCatalog.WhatsApp,
      ct
    );
    if (
      !IntegrationProviderCatalog.IsConfigured(
        IntegrationProviderCatalog.WhatsApp,
        values
      )
    )
      return null;
    var phoneNumberId = values.Get("phoneNumberId")!.Trim();
    // The id becomes part of the request path.
    if (!Digits().IsMatch(phoneNumberId))
      return null;
    return new(
      phoneNumberId,
      values.Get("accessToken")!.Trim(),
      values.Get("appSecret")!.Trim(),
      values.Get("verifyToken")!.Trim()
    );
  }

  private string Version()
  {
    var configured = configuration["WhatsApp:GraphApiVersion"]?.Trim();
    return configured is not null && GraphVersion().IsMatch(configured)
      ? configured
      : DefaultGraphVersion;
  }

  private static string? MessageId(string body)
  {
    try
    {
      using var document = JsonDocument.Parse(body);
      return
        document.RootElement.TryGetProperty("messages", out var messages)
        && messages.ValueKind == JsonValueKind.Array
        && messages.GetArrayLength() > 0
        && messages[0].TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
        && id.GetString() is { Length: > 0 and <= 200 } value
        ? value
        : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private static int? ErrorCode(string body)
  {
    try
    {
      using var document = JsonDocument.Parse(body);
      return
        document.RootElement.TryGetProperty("error", out var error)
        && error.ValueKind == JsonValueKind.Object
        && error.TryGetProperty("code", out var code)
        && code.TryGetInt32(out var value)
        ? value
        : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  [GeneratedRegex("^[0-9]{5,30}$")]
  private static partial Regex Digits();

  [GeneratedRegex("^v[0-9]{1,3}\\.0$")]
  private static partial Regex GraphVersion();
}
