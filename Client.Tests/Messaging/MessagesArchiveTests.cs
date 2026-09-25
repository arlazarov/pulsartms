using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Tests.Support;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// Archive is its own chip: it asks for the chats of drivers no longer
// active, shows each with an "Inactive driver" tag (the driver's status,
// not a message's), and lists no drivers to start a chat with.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class MessagesArchiveTests
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

  [Fact]
  public async Task ArchiveListsInactiveDriversChatsTaggedAndNoDirectory()
  {
    var api = new Api();
    await using var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.JSInterop.SetupModule("./js/generated/shared/messagingChannel.js");
    context.JSInterop.SetupModule("./js/generated/shared/messagingNotices.js");
    var page = context.Render<MessagesPage>();
    page.WaitForAssertion(() => Assert.Contains("Ann Active", page.Markup));
    page.Settle();
    Assert.DoesNotContain("Inactive driver", page.Markup);
    var directories = api.DriverQueries;

    await page.FindAll("button.messages__chip")
      .Single(x => x.TextContent.Trim() == "Archive")
      .ClickAsync(new());

    page.WaitForAssertion(() => Assert.Contains("Bo Gone", page.Markup));
    page.Settle();
    Assert.Contains("archived=true", api.Inboxes[^1]);
    Assert.DoesNotContain("Ann Active", page.Markup);
    Assert.Contains(
      "Inactive driver",
      page.Find(".messages__conversations li").TextContent
    );
    Assert.Equal(
      "true",
      page.FindAll("button.messages__chip")
        .Single(x => x.TextContent.Trim() == "Archive")
        .GetAttribute("aria-pressed")
    );
    Assert.Empty(page.FindAll("section[aria-label='Drivers without a chat']"));
    Assert.Equal(directories, api.DriverQueries);
  }

  private sealed class Api
  {
    public List<string> Inboxes { get; } = [];
    public int DriverQueries { get; private set; }

    private static ConversationSummary Summary(string name, bool active) =>
      new(
        Guid.NewGuid(),
        "+15550000000",
        Guid.NewGuid(),
        name,
        "hello",
        Now,
        Now,
        true,
        0,
        null,
        null,
        1
      )
      {
        DriverActive = active,
      };

    public Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      if (path == "/api/messaging/templates")
        return Ok<IReadOnlyList<MessageTemplateView>>([]);
      if (path == "/api/messaging/inbox")
      {
        var query = request.RequestUri.Query;
        Inboxes.Add(query);
        return Ok(
          new InboxView(
            [
              query.Contains("archived=true")
                ? Summary("Bo Gone", false)
                : Summary("Ann Active", true),
            ],
            false
          )
        );
      }
      if (path == "/api/messaging/drivers")
      {
        DriverQueries++;
        return Ok(new MessagingDriversView([], true, false));
      }
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static Task<HttpResponseMessage> Ok<T>(T response) =>
      Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(
            new RequestResponseDTO<T> { Success = true, Response = response }
          ),
        }
      );
  }
}
