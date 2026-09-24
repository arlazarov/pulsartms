using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Services;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// New chat lists the drivers with a WhatsApp number; choosing one asks the
// server for the driver's conversation and opens it. Nothing is sent from
// here, and while WhatsApp is not set up no driver can be chosen.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class NewChatTests
{
  private static readonly Guid Ann = Guid.NewGuid();
  private static readonly Guid Chat = Guid.NewGuid();

  [Fact]
  public async Task ChoosingADriverOpensTheirConversation()
  {
    var api = new Api(configured: true);
    await using var context = Context(api);
    var page = context.Render<MessagesPage>();
    page.WaitForAssertion(() => Assert.Equal(1, api.InboxReads));

    await page.FindAll("button")
      .Single(x => x.TextContent.Trim() == "New chat")
      .ClickAsync(new());
    page.WaitForAssertion(() => Assert.Contains("Ann Lee", page.Markup));
    Assert.Contains("+15550000001", page.Markup);
    Assert.Contains("api/messaging/drivers", api.DriverQueries.Single());

    await page.FindAll("button.messages__conversation")
      .Single()
      .ClickAsync(new());

    page.WaitForAssertion(
      () =>
        Assert.EndsWith(
          $"/messages/{Chat}",
          context.Services.GetRequiredService<NavigationManager>().Uri
        )
    );
    Assert.Equal([$"/api/messaging/drivers/{Ann}/conversation"], api.Opens);
    Assert.Empty(api.Sends);
    Assert.True(api.InboxReads >= 2);
  }

  [Fact]
  public async Task WithoutWhatsAppSetUpNoDriverCanBeChosen()
  {
    var api = new Api(configured: false);
    await using var context = Context(api);
    var picker = context.Render<Client.Pages.Messages.NewChat>();

    picker.WaitForAssertion(
      () => Assert.Contains("WhatsApp is not set up", picker.Markup)
    );
    var driver = picker.Find("button.messages__conversation");
    Assert.True(driver.HasAttribute("disabled"));
    await driver.ClickAsync(new());
    Assert.Empty(api.Opens);
  }

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
    public int InboxReads { get; private set; }

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
        InboxReads++;
        return Ok(new InboxView([], false));
      }
      if (path == "/api/messaging/drivers")
      {
        DriverQueries.Add(request.RequestUri.PathAndQuery);
        return Ok(
          new MessagingDriversView(
            [new(Ann, "Ann Lee", "+15550000001", null)],
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
      if (path.Contains("/messages") || path.Contains("/templates"))
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
