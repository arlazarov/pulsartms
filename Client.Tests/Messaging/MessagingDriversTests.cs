using System.Net;
using System.Net.Http.Json;
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

  [Fact]
  public async Task ChoosingADriverOpensTheirConversation()
  {
    var api = new Api(configured: true);
    await using var context = Context(api);
    var page = context.Render<MessagesPage>();
    page.WaitForAssertion(() => Assert.Contains("Ann Lee", page.Markup));
    page.Settle();
    Assert.Contains("Drivers without a chat", page.Markup);
    Assert.Contains("withoutConversation=true", api.DriverQueries.Single());

    await Driver(page, "Ann Lee").ClickAsync(new());
    page.WaitForAssertion(
      () => Assert.EndsWith($"/messages/{Chat}", Uri(context))
    );
    Assert.Equal([$"/api/messaging/drivers/{Ann}/conversation"], api.Opens);

    page.Settle();
    await Driver(page, "Bo Diaz").ClickAsync(new());
    page.WaitForAssertion(
      () => Assert.EndsWith($"/messages/{Known}", Uri(context))
    );
    Assert.Single(api.Opens);
    Assert.Empty(api.Sends);
  }

  [Fact]
  public async Task TheSearchNarrowsDriversAndUnreadHidesThem()
  {
    var api = new Api(configured: true);
    await using var context = Context(api);
    var page = context.Render<MessagesPage>();
    page.WaitForAssertion(() => Assert.Contains("Ann Lee", page.Markup));
    page.Settle();

    page.Find(".messages__search input").Change("ann");
    await page.Find(".messages__search").SubmitAsync();
    page.WaitForAssertion(
      () => Assert.Contains("search=ann", api.DriverQueries[^1])
    );

    page.Settle();
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
    var api = new Api(configured: false);
    await using var context = Context(api);
    var page = context.Render<MessagesPage>();

    page.WaitForAssertion(
      () => Assert.Contains("WhatsApp is not set up", page.Markup)
    );
    page.Settle();
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

  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
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

    public Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      if (path == "/api/messaging/templates")
        return Ok<IReadOnlyList<MessageTemplateView>>([]);
      if (path == "/api/messaging/inbox")
        return Ok(new InboxView([], false));
      if (path == "/api/messaging/drivers")
      {
        DriverQueries.Add(request.RequestUri.Query);
        return Ok(
          new MessagingDriversView(
            [
              new(Ann, "Ann Lee", "+15550000001", null),
              new(Bo, "Bo Diaz", "+15550000002", Known),
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
