using Domain.Models.Messaging;

namespace Domain.Rules.Messaging;

public static class DriverMessageProgress
{
  // WhatsApp's customer service window.
  public static readonly TimeSpan SessionWindow = TimeSpan.FromHours(24);

  // A message waiting this long for its provider's answer is not known.
  public static readonly TimeSpan SendingTimeout = TimeSpan.FromMinutes(2);

  // Provider notifications arrive out of order and more than once. Only a
  // message the provider took moves at all; a status only moves forward, a
  // failure ends a message that was not yet delivered, and nothing moves a
  // failed or read message back.
  public static bool Advances(string current, string next) =>
    next == DriverMessageStatuses.Failed
      ? Rank(current) is 0 or 1
      : Rank(current) >= 0 && Rank(next) > Rank(current);

  private static int Rank(string status) =>
    status switch
    {
      DriverMessageStatuses.Accepted => 0,
      DriverMessageStatuses.Sent => 1,
      DriverMessageStatuses.Delivered => 2,
      DriverMessageStatuses.Read => 3,
      _ => -1,
    };

  // The provider has it, and it has not failed since.
  public static bool Taken(string status) => Rank(status) >= 0;

  // Nobody knows whether it went: sending again needs a dispatcher to say so.
  public static bool Uncertain(
    string status,
    DateTime statusAt,
    DateTime now
  ) =>
    status == DriverMessageStatuses.Unknown
    || status == DriverMessageStatuses.Sending
      && now - statusAt >= SendingTimeout;

  public static bool InProgress(
    string status,
    DateTime statusAt,
    DateTime now
  ) =>
    status == DriverMessageStatuses.Sending && now - statusAt < SendingTimeout;

  public static bool WindowOpen(DateTime? lastInbound, DateTime now) =>
    lastInbound is { } at
    && now - at < SessionWindow
    && at <= now.AddMinutes(5);
}
