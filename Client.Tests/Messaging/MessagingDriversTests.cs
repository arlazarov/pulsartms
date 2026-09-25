using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// Below the conversations the page lists the drivers with a WhatsApp
// number and no chat yet, under the same search. Choosing one asks the
// server for the driver's conversation and opens it; nothing is sent. A
// driver already known to have one opens it without asking, and while
// WhatsApp is not set up no driver can be chosen.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class MessagingDriversTests
{
  private static readonly Guid Ann = Guid.NewGuid();
  private static readonly Guid Bo = Guid.NewGuid();
  private static readonly Guid Chat = Guid.NewGuid();
  private static readonly Guid Known = Guid.NewGuid();
  private static readonly Guid Cy = Guid.NewGuid();
  private static readonly Guid OtherChat = Guid.NewGuid();

  [Fact]
  public async Task ChoosingADriverOpensTheirConversation()
  {
    var api = new Api(configured: true) { Other = true };
    await using var context = Context(api);
    var page = context.Render<MessagesPage>();
    Loaded(page);
    // Named for screen readers; no visible heading above the rows.
    page.Find("section[aria-label='Drivers without a chat']");
    Assert.DoesNotContain(
      page.FindAll("h2, h3"),
      x => x.TextContent.Contains("Drivers without a chat")
    );
    Assert.Contains("withoutConversation=true", api.DriverQueries.Single());

    var reread = Reread(context, api, out var list);
    await Driver(page, "Ann Lee").ClickAsync(new());
    page.WaitForAssertion(
      () => Assert.EndsWith($"/messages/{Chat}", Uri(context))
    );
    Assert.Equal([$"/api/messaging/drivers/{Ann}/conversation"], api.Opens);
    reread.Release(page, () => Assert.True(ListShown(page) >= list));
    // An opened chat with no message is not listed: Ann keeps her place,
    // and choosing her again opens the same chat without asking again.
    Assert.Equal(
      ["Ann Lee", "Bo Diaz", "Cy Wrong"],
      page.FindAll(".messages__drivers .messages__who strong")
        .Select(x => x.TextContent.Trim())
    );
    reread = Reread(context, api, out list);
    await Driver(page, "Ann Lee").ClickAsync(new());
    Assert.Single(api.Opens);
    reread.Release(page, () => Assert.True(ListShown(page) >= list));

    // A phone used for WhatsApp says so; an invalid WhatsApp number cannot
    // be opened, and the phone is not used in its place.
    Assert.Contains("+15550000002 (phone)", page.Markup);
    Assert.Contains("WhatsApp number is not valid", page.Markup);
    Assert.True(Driver(page, "Cy Wrong").HasAttribute("disabled"));

    await Driver(page, "Bo Diaz").ClickAsync(new());
    page.WaitForAssertion(
      () => Assert.EndsWith($"/messages/{Known}", Uri(context))
    );
    Assert.Single(api.Opens);
    Assert.Empty(api.Sends);
  }

  // Choosing a chat, even the one already open, reads the list again, and
  // that answer draws the drivers again, replacing their buttons' handlers.
  // A driver found before it and clicked after it is gone
  // (UnknownEventHandlerIdException, seen in the gate), which is why the
  // test above waits for that answer to be on screen before choosing again.
  [Fact]
  public async Task ChoosingAChatReadsTheListAndDrawsTheDriversAgain()
  {
    var api = new Api(configured: true) { Other = true };
    await using var context = Context(api);
    var page = context.Render<MessagesPage>();
    Loaded(page);
    var reread = Reread(context, api, out var list);
    await Driver(page, "Ann Lee").ClickAsync(new());
    reread.WaitAsked();
    var before = Driver(page, "Bo Diaz").GetAttribute("blazor:onclick");

    reread.Release(page, () => Assert.True(ListShown(page) >= list));

    Assert.NotNull(before);
    Assert.NotEqual(
      before,
      Driver(page, "Bo Diaz").GetAttribute("blazor:onclick")
    );
  }

  // The page's own first reads are on screen: the drivers and the first
  // list answer. The templates are held (Context), and nothing else is read
  // until the test acts.
  private static void Loaded(IRenderedComponent<MessagesPage> page) =>
    page.WaitForAssertion(() =>
    {
      Assert.Contains("Ann Lee", page.Markup);
      Assert.Equal(1, ListShown(page));
    });

  // The next list read, held: the list shows its number once applied.
  private static HeldRequest Reread(
    ClientComponentContext context,
    Api api,
    out int number
  )
  {
    number = api.Lists + 1;
    return context.Hold("/api/messaging/inbox");
  }

  private static int ListShown(IRenderedComponent<MessagesPage> page) =>
    page.FindAll(".messages__conversation")
      .Select(row => Regex.Match(row.TextContent, @"\[list (\d+)\]"))
      .Where(match => match.Success)
      .Select(match => int.Parse(match.Groups[1].Value))
      .DefaultIfEmpty(0)
      .Max();

  // Once a driver's chat is in the list above (it holds a message), their
  // row leaves the directory; nothing else moves.
  [Fact]
  public async Task AListedChatTakesItsDriverOutOfTheDirectory()
  {
    var api = new Api(configured: true) { ListedChat = Known, Other = true };
    await using var context = Context(api);
    var page = context.Render<MessagesPage>();
    Loaded(page);

    Assert.Equal(
      ["Ann Lee", "Cy Wrong"],
      page.FindAll(".messages__drivers .messages__who strong")
        .Select(x => x.TextContent.Trim())
    );
  }

  [Fact]
  public async Task TheSearchNarrowsDriversAndUnreadHidesThem()
  {
    var api = new Api(configured: true) { Other = true };
    await using var context = Context(api);
    var page = context.Render<MessagesPage>();
    Loaded(page);

    var list = api.Lists + 1;
    page.Find(".messages__search input").Change("ann");
    await page.Find(".messages__search").SubmitAsync();
    // The search reads the list and the drivers again; both answers are on
    // screen before the next choice.
    page.WaitForAssertion(() =>
    {
      Assert.Contains("search=ann", api.DriverQueries[^1]);
      Assert.True(ListShown(page) >= list);
    });
    await page.FindAll(".messages__chip")
      .Single(x => x.TextContent.Trim() == "Unread")
      .ClickAsync(new());
    page.WaitForAssertion(
      () => Assert.DoesNotContain("Drivers without a chat", page.Markup)
    );
  }

  [Fact]
  public async Task WithoutWhatsAppSetUpNoDriverCanBeChosen()
  {
    var api = new Api(configured: false) { Other = true };
    await using var context = Context(api);
    var page = context.Render<MessagesPage>();

    Loaded(page);
    Assert.Contains("WhatsApp is not set up", page.Markup);
    var driver = Driver(page, "Ann Lee");
    Assert.True(driver.HasAttribute("disabled"));
    await driver.ClickAsync(new());
    Assert.Empty(api.Opens);
  }

  private static AngleSharp.Dom.IElement Driver(
    IRenderedComponent<MessagesPage> page,
    string name
  ) =>
    page.FindAll("button.messages__conversation")
      .Single(x => x.TextContent.Contains(name));

  private static string Uri(ClientComponentContext context) =>
    context.Services.GetRequiredService<NavigationManager>().Uri;

  // The templates matter only to the composer and the group message, which
  // no test here reads; their answer would render the page again (and the
  // drivers with it) at a moment of its own, so it is held until the end.
  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.Hold("/api/messaging/templates");
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.JSInterop.SetupModule("./js/generated/shared/messagingChannel.js");
    context.JSInterop.SetupModule("./js/generated/shared/messagingNotices.js");
    return context;
  }

  private sealed class Api(bool configured)
  {
    public List<string> DriverQueries { get; } = [];
    public List<string> Opens { get; } = [];
    public List<string> Sends { get; } = [];

    public Guid? ListedChat { get; init; }

    // A chat with no driver of these, listed only so that each list answer
    // shows its number: `[list n]` in its preview.
    public bool Other { get; init; }
    private int _lists;
    public int Lists => Volatile.Read(ref _lists);

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
        var list = Interlocked.Increment(ref _lists);
        return Ok(
          new InboxView(
            [
              .. ListedChat is { } listed
                ?
                [
                  new ConversationSummary(
                    listed,
                    "+15550000002",
                    Bo,
                    "Bo Diaz",
                    "hello",
                    DateTime.UtcNow,
                    DateTime.UtcNow,
                    true,
                    0,
                    null,
                    null,
                    1
                  ),
                ]
                : Array.Empty<ConversationSummary>(),
              .. Other
                ?
                [
                  new ConversationSummary(
                    OtherChat,
                    "+15550000009",
                    null,
                    "Dee Other",
                    $"hello [list {list}]",
                    DateTime.UtcNow,
                    DateTime.UtcNow,
                    true,
                    0,
                    null,
                    null,
                    1
                  ),
                ]
                : Array.Empty<ConversationSummary>(),
            ],
            false
          )
        );
      }
      if (path == "/api/messaging/drivers")
      {
        DriverQueries.Add(request.RequestUri.Query);
        return Ok(
          new MessagingDriversView(
            [
              new(Ann, "Ann Lee", "+15550000001", "whatsapp", null),
              new(Bo, "Bo Diaz", "+15550000002", "phone", Known),
              new(Cy, "Cy Wrong", null, "invalidWhatsApp", null),
            ],
            configured,
            false
          )
        );
      }
      if (request.Method == HttpMethod.Post && path.EndsWith("/conversation"))
      {
        Opens.Add(path);
        return Ok(Chat);
      }
      if (path.EndsWith("/messages") || path.EndsWith("/templates"))
        Sends.Add(path);
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
