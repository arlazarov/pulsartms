using System.Security.Cryptography;
using Application.Features.Messaging.Interfaces;
using Domain.Models.Messaging;

namespace Server.Tests.Support;

// A transport that records what it was asked to send and answers as told.
// During runs inside the call, while the message is in flight.
internal sealed class FakeDriverMessaging : IDriverMessaging
{
  public Queue<DriverMessageSendResult> Answers { get; } = [];
  public List<(string To, string Text)> Sent { get; } = [];
  public Func<Task>? During { get; set; }

  // Runs as a send begins, before the adapter reads its credentials: what
  // changes here happened after the caller's own checks.
  public Action? BeforeSend { get; set; }
  public bool Configured { get; set; } = true;

  // Files the provider holds, by media id; a declared hash may be set to
  // differ from the bytes. Unavailable makes every download fail for now.
  public Dictionary<
    string,
    (byte[] Bytes, string Type, string? Sha)
  > Media { get; } = [];
  public bool Unavailable { get; set; }
  public int Downloads { get; private set; }
  public DriverMessagingNotification? Notification { get; set; }

  // Runs while a download is being opened, as another pass would meanwhile.
  public Func<Task>? DuringMedia { get; set; }

  public string Channel => DriverMessageChannels.WhatsApp;

  public Task<bool> IsConfiguredAsync(CancellationToken ct) =>
    Task.FromResult(Configured);

  public string? BusinessNumber { get; set; } = "123456";

  public Task<string?> BusinessNumberAsync(CancellationToken ct) =>
    Task.FromResult(Configured ? BusinessNumber : null);

  // A send whose number is no longer the configured one never reaches
  // the provider, as the real adapter answers it.
  private bool Changed(string businessNumber)
  {
    BeforeSend?.Invoke();
    return !Configured || businessNumber != BusinessNumber;
  }

  public async Task<DriverMessageSendResult> SendTextAsync(
    string businessNumber,
    string recipient,
    string text,
    CancellationToken ct
  )
  {
    if (Changed(businessNumber))
      return new(DriverMessageOutcome.NumberChanged);
    Sent.Add((recipient, text));
    if (During is { } during)
      await during();
    return Answers.Count > 0
      ? Answers.Dequeue()
      : new(DriverMessageOutcome.Accepted, $"wamid.{Sent.Count}");
  }

  public List<(
    string To,
    string Name,
    byte[] Bytes,
    string Caption
  )> Files { get; } = [];
  public List<(
    string To,
    string Name,
    IReadOnlyList<string> Parameters
  )> Templates { get; } = [];

  public async Task<DriverMessageSendResult> SendFileAsync(
    string businessNumber,
    string recipient,
    DriverFile file,
    CancellationToken ct
  )
  {
    if (Changed(businessNumber))
      return new(DriverMessageOutcome.NumberChanged);
    using var copy = new MemoryStream();
    await file.Content.CopyToAsync(copy, ct);
    Files.Add((recipient, file.FileName, copy.ToArray(), file.Caption));
    return Answers.Count > 0
      ? Answers.Dequeue()
      : new(DriverMessageOutcome.Accepted, $"wamid.file.{Files.Count}");
  }

  public Task<DriverMessageSendResult> SendTemplateAsync(
    string businessNumber,
    string recipient,
    string name,
    string language,
    IReadOnlyList<string> parameters,
    CancellationToken ct
  )
  {
    if (Changed(businessNumber))
      return Task.FromResult(
        new DriverMessageSendResult(DriverMessageOutcome.NumberChanged)
      );
    Templates.Add((recipient, name, parameters));
    return Task.FromResult(
      Answers.Count > 0
        ? Answers.Dequeue()
        : new DriverMessageSendResult(
          DriverMessageOutcome.Accepted,
          $"wamid.template.{Templates.Count}"
        )
    );
  }

  public Task<bool> AcceptsSubscriptionAsync(
    string? verifyToken,
    CancellationToken ct
  ) => Task.FromResult(false);

  public Task<DriverMessagingNotification?> ReadNotificationAsync(
    ReadOnlyMemory<byte> body,
    string? signature,
    CancellationToken ct
  ) => Task.FromResult(Notification);

  public async Task<DriverMedia?> OpenMediaAsync(
    string mediaId,
    CancellationToken ct
  )
  {
    Downloads++;
    if (DuringMedia is { } during)
      await during();
    if (Unavailable)
      throw new DriverMessagingUnavailableException("No answer.");
    return Media.TryGetValue(mediaId, out var media)
      ? new(
        new MemoryStream(media.Bytes),
        media.Bytes.Length,
        media.Type,
        media.Sha ?? Convert.ToHexStringLower(SHA256.HashData(media.Bytes))
      )
      : null;
  }
}
