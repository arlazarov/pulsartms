using Bunit;
using Client.Models.DTO.Messaging;
using Client.Pages.Messages;
using Client.Tests.Support;

namespace Client.Tests.Messaging;

// Send again is offered only on a reply's latest attempt: one already sent
// again says so, since the server refuses to send it once more.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class MessageItemTests
{
  [Theory]
  [InlineData(false, true)]
  [InlineData(true, false)]
  public void OnlyTheLatestAttemptOffersSendAgain(bool retried, bool offered)
  {
    using var context = new ClientComponentContext(
      (_, _) => Task.FromResult(new HttpResponseMessage())
    );
    var item = context.Render<MessageItem>(x =>
      x.Add(
        p => p.Message,
        new MessageView(
          Guid.NewGuid(),
          "out",
          "text",
          "On my way",
          "rejected",
          DateTime.UtcNow,
          "You",
          190,
          []
        )
        {
          Retried = retried,
        }
      )
    );

    Assert.Equal(
      offered,
      item.FindAll("button").Any(x => x.TextContent.Trim() == "Send again")
    );
    Assert.Equal(retried, item.Markup.Contains("sent again below"));
  }
}
