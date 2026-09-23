using Domain.Models.Messaging;

namespace Application.Features.Routing.Interfaces;

// A provider that carries messages to drivers, for the serving company and
// with that company's credentials. It never retries a send: an answer it
// did not get is reported as unknown.
public interface IDriverMessaging
{
  string Channel { get; }

  Task<bool> IsConfiguredAsync(CancellationToken ct);

  Task<DriverMessageSendResult> SendTextAsync(
    string recipient,
    string text,
    CancellationToken ct
  );

  // Whether a subscription check carries this company's verify token.
  Task<bool> AcceptsSubscriptionAsync(
    string? verifyToken,
    CancellationToken ct
  );

  // The events of a notification whose signature matches this company's
  // app secret, or null when it does not or nothing is configured.
  Task<DriverMessagingNotification?> ReadNotificationAsync(
    ReadOnlyMemory<byte> body,
    string? signature,
    CancellationToken ct
  );
}
