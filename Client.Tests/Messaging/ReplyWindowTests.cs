using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// The reply window says how long WhatsApp still takes free-form replies,
// in hours, then minutes, counted from the driver's last message.
[Trait("Category", "Messaging")]
[Trait("Kind", "Unit")]
public sealed class ReplyWindowTests
{
  private static readonly DateTime Now = new(
    2026,
    9,
    25,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  [Theory]
  [InlineData(3 * 60, "Reply window: 21 hours left")]
  [InlineData(22 * 60 + 30, "Reply window: 1 hour left")]
  [InlineData(23 * 60 + 15, "Reply window: 45 minutes left")]
  [InlineData(23 * 60 + 59, "Reply window: 1 minute left")]
  public void TheWindowCountsDownFromTheDriversLastMessage(
    int minutesAgo,
    string expected
  ) =>
    Assert.Equal(
      expected,
      MessagesPage.ReplyWindow(Now.AddMinutes(-minutesAgo), Now)
    );
}
