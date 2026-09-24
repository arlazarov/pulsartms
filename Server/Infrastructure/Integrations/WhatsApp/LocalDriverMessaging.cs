using System.Security.Cryptography;
using System.Text;
using Application.Features.Messaging.Interfaces;
using Domain.Models.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Infrastructure.Integrations.WhatsApp;

// A stand-in for WhatsApp on a developer's machine, chosen only by
// WhatsApp:Provider = "local" and refused outside Development, at start and
// here. Nothing leaves the process: every send is accepted under a local id.
// Driver messages are simulated through the ordinary webhook, in the Cloud
// API's shape, signed with WhatsApp:Local:AppSecret; every media id opens
// the same small synthetic image, so the capture pipeline can be followed.
public sealed class LocalDriverMessaging : IDriverMessaging
{
  public const string ProviderName = "local";
  public const string DevelopmentOnly =
    "The local messaging provider runs only in Development.";

  private static readonly byte[] Image = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/"
      + "iZk9HQAAAABJRU5ErkJggg=="
  );

  private readonly LocalMessagingOptions _options;

  public LocalDriverMessaging(
    IOptions<LocalMessagingOptions> options,
    IHostEnvironment environment
  )
  {
    if (!environment.IsDevelopment())
      throw new InvalidOperationException(DevelopmentOnly);
    _options = options.Value;
  }

  public string Channel => DriverMessageChannels.WhatsApp;

  public Task<bool> IsConfiguredAsync(CancellationToken ct) =>
    Task.FromResult(true);

  public Task<string?> BusinessNumberAsync(CancellationToken ct) =>
    Task.FromResult<string?>(_options.PhoneNumberId);

  public Task<DriverMessageSendResult> SendTextAsync(
    string recipient,
    string text,
    CancellationToken ct
  ) => Accepted();

  public async Task<DriverMessageSendResult> SendFileAsync(
    string recipient,
    DriverFile file,
    CancellationToken ct
  )
  {
    // Read to the end, as an upload would, so a broken stream shows here.
    await file.Content.CopyToAsync(Stream.Null, ct);
    return await Accepted();
  }

  public Task<DriverMessageSendResult> SendTemplateAsync(
    string recipient,
    string name,
    string language,
    IReadOnlyList<string> parameters,
    CancellationToken ct
  ) => Accepted();

  public Task<bool> AcceptsSubscriptionAsync(
    string? verifyToken,
    CancellationToken ct
  ) =>
    Task.FromResult(
      !string.IsNullOrEmpty(verifyToken)
        && !string.IsNullOrEmpty(_options.VerifyToken)
        && CryptographicOperations.FixedTimeEquals(
          Encoding.UTF8.GetBytes(verifyToken),
          Encoding.UTF8.GetBytes(_options.VerifyToken)
        )
    );

  public Task<DriverMessagingNotification?> ReadNotificationAsync(
    ReadOnlyMemory<byte> body,
    string? signature,
    CancellationToken ct
  ) =>
    Task.FromResult(
      !string.IsNullOrEmpty(_options.AppSecret)
      && WhatsAppCloudMessaging.Signed(body.Span, signature, _options.AppSecret)
        ? WhatsAppNotifications.Read(body, _options.PhoneNumberId)
        : null
    );

  public Task<DriverMedia?> OpenMediaAsync(
    string mediaId,
    CancellationToken ct
  ) =>
    Task.FromResult<DriverMedia?>(
      new(
        new MemoryStream(Image, writable: false),
        Image.Length,
        "image/png",
        Convert.ToHexStringLower(SHA256.HashData(Image))
      )
    );

  private static Task<DriverMessageSendResult> Accepted() =>
    Task.FromResult(
      new DriverMessageSendResult(
        DriverMessageOutcome.Accepted,
        $"local.{Guid.NewGuid():N}"
      )
    );
}

public sealed class LocalMessagingOptions
{
  public string PhoneNumberId { get; set; } = "local";
  public string? AppSecret { get; set; }
  public string? VerifyToken { get; set; }
}
