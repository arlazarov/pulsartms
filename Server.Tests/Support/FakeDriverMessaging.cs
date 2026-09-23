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

  public string Channel => DriverMessageChannels.WhatsApp;

  public Task<bool> IsConfiguredAsync(CancellationToken ct) =>
    Task.FromResult(Configured);

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
  ) => Task.FromResult<DriverMessagingNotification?>(null);
}
