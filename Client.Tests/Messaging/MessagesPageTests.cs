using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// The Messages page reads the inbox and the open conversation, marks it
// read through its newest message, keeps a reply's retry key until it is
// sent, offers to confirm a reply refused as stale, and reads the
// conversation again when the browser's stream says it changed.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class MessagesPageTests
{
  private static readonly Guid Ann = Guid.NewGuid();
  private static readonly DateTime Now = new(
    2026,
    9,
    21,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  [Fact]
  public async Task TheInboxAndTheConversationReadAsTheDispatcherExpects()
  {
    var api = new Api(windowOpen: true);
    await using var context = Context(api);

    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));

    page.WaitForAssertion(() => Assert.Contains("Delivered", page.Markup));
    Assert.Equal("2", page.Find(".messages__unread").TextContent.Trim());
    Assert.Contains("Bob is replying", page.Markup);
    var items = page.FindAll(".messages__item");
    Assert.Contains("Where do I fuel?", items[0].TextContent);
    Assert.Contains("At the Pilot", items[1].TextContent);
    Assert.Equal(3, api.ReadRevision);
    Assert.NotNull(page.Find("#messages-text"));
  }

  [Fact]
  public async Task AStaleReplyIsConfirmedWithTheSameRetryKey()
  {
    var api = new Api(windowOpen: true) { RefuseFirstAsStale = true };
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));
    page.WaitForElement("#messages-text").Input("Ok");

    await page.Find(".messages__composer").SubmitAsync();
    page.WaitForAssertion(() => Assert.Contains("newer message", page.Markup));
    await page.FindAll("button")
      .Single(x => x.TextContent.Contains("Send anyway"))
      .ClickAsync(new());

    Assert.Equal(2, api.Sends.Count);
    Assert.Equal(api.Sends[0].IdempotencyKey, api.Sends[1].IdempotencyKey);
    Assert.Equal((false, true), (api.Sends[0].Confirm, api.Sends[1].Confirm));
    Assert.Equal(
      api.Sends[0].LastSeenMessageId,
      api.Sends[1].LastSeenMessageId
    );
    page.WaitForAssertion(
      () =>
        Assert.Equal(
          "",
          page.Find("#messages-text").GetAttribute("value") ?? ""
        )
    );
  }

  [Fact]
  public async Task OutsideTheWindowWithoutApprovedTemplatesNothingCanBeSent()
  {
    await using var context = Context(new Api(windowOpen: false));

    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));

    page.WaitForAssertion(
      () => Assert.Contains("No template is approved yet", page.Markup)
    );
    Assert.Empty(page.FindAll("#messages-text"));
  }

  [Fact]
  public async Task AStreamSignalForTheOpenConversationReadsItAgain()
  {
    var api = new Api(windowOpen: true);
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));
    page.WaitForAssertion(() => Assert.Equal(1, api.ThreadReads));

    context
      .Services.GetRequiredService<MessagingSignals>()
      .Receive("change", Ann.ToString());
    page.WaitForAssertion(() => Assert.Equal(2, api.ThreadReads));

    context
      .Services.GetRequiredService<MessagingSignals>()
      .Receive("change", Guid.NewGuid().ToString());
    page.WaitForAssertion(() => Assert.True(api.InboxReads >= 3));
    Assert.Equal(2, api.ThreadReads);
  }

  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.JSInterop.SetupModule("./js/generated/shared/messagingChannel.js");
    context.Services.AddSingleton<TokenStorageService>();
    context.Services.AddSingleton<MessagingSignals>();
    context.JSInterop.SetupModule("./js/generated/shared/messagingNotices.js");
    return context;
  }

  private sealed class Api(bool windowOpen)
  {
    public bool RefuseFirstAsStale { get; init; }
    public List<SendMessageRequest> Sends { get; } = [];
    public long? ReadRevision { get; private set; }
    public int ThreadReads { get; private set; }
    public int InboxReads { get; private set; }

    private ConversationSummary Summary(
      Guid id,
      string name,
      int unread,
      string? claimed
    ) =>
      new(
        id,
        "+15558234327",
        Guid.NewGuid(),
        name,
        "At the Pilot",
        Now.AddMinutes(5),
        Now,
        windowOpen,
        unread,
        claimed,
        claimed is null ? null : Now.AddMinutes(1),
        3
      );

    public async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      if (path == "/api/messaging/templates")
        return Ok<IReadOnlyList<MessageTemplateView>>([]);
      if (path == "/api/messaging/inbox")
      {
        InboxReads++;
        return Ok(new InboxView([Summary(Ann, "Ann Driver", 2, "Bob")], false));
      }
      if (
        path == $"/api/messaging/conversations/{Ann}"
        && request.Method == HttpMethod.Get
      )
      {
        ThreadReads++;
        return Ok(
          new ConversationView(
            Summary(Ann, "Ann Driver", 2, null),
            [
              new(
                Guid.NewGuid(),
                "out",
                "text",
                "At the Pilot",
                "delivered",
                Now.AddMinutes(5),
                "Bob",
                null,
                []
              ),
              new(
                Guid.NewGuid(),
                "in",
                "text",
                "Where do I fuel?",
                "received",
                Now,
                null,
                null,
                []
              ),
            ],
            false
          )
        );
      }
      if (path.EndsWith("/read"))
      {
        ReadRevision = (
          await request.Content!.ReadFromJsonAsync<ReadRequest>(ct)
        )!.Revision;
        return Ok(true);
      }
      if (path.EndsWith("/messages"))
      {
        Sends.Add(
          (await request.Content!.ReadFromJsonAsync<SendMessageRequest>(ct))!
        );
        if (RefuseFirstAsStale && Sends.Count == 1)
          return new(HttpStatusCode.Conflict)
          {
            Content = JsonContent.Create(
              new RequestResponseDTO<MessageView>
              {
                Success = false,
                Errors =
                [
                  "A newer message arrived since you started. Read it, "
                    + "then send again or confirm.",
                ],
              }
            ),
          };
        return Ok(
          new MessageView(
            Guid.NewGuid(),
            "out",
            "text",
            "Ok",
            "queued",
            Now,
            null,
            null,
            []
          )
        );
      }
      if (path.EndsWith("/claim"))
        return Ok(true);
      return new(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Ok<T>(T body) =>
      new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<T> { Success = true, Response = body }
        ),
      };
  }
}
