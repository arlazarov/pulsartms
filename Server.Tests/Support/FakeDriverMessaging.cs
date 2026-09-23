using System.Security.Cryptography;
using Application.Features.Routing.Interfaces;
using Domain.Models.Messaging;

namespace Server.Tests.Support;

// A transport that records what it was asked to send and answers as told.
// During runs inside the call, while the message is in flight.
internal sealed class FakeDriverMessaging : IDriverMessaging
{
  public Queue<DriverMessageSendResult> Answers { get; } = [];
  public List<(string To, string Text)> Sent { get; } = [];
  public Func<Task>? During { get; set; }
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

  public async Task<DriverMessageSendResult> SendTextAsync(
    string recipient,
    string text,
    CancellationToken ct
  )
  {
    Sent.Add((recipient, text));
    if (During is { } during)
      await during();
    return Answers.Count > 0
      ? Answers.Dequeue()
      : new(DriverMessageOutcome.Accepted, $"wamid.{Sent.Count}");
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
