using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Services;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// Messages opened without a conversation opens where the dispatcher left
// it in this tab, else the newest conversation by its last message - where
// the conversation stands beside the list. A phone keeps its list. One
// opened so is not marked read until the dispatcher picks it or starts a
// reply; a link to a conversation opens that one and reads it as before.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class MessagesOpeningTests
{
  private static readonly Guid Older = Guid.NewGuid();
  private static readonly Guid Newest = Guid.NewGuid();
  private static readonly DateTime Now = new(
    2026,
    9,
    26,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  [Fact]
  public async Task AFirstVisitOpensTheNewestWithoutReadingIt()
  {
    var api = new Api();
    await using var context = Context(api, beside: true);
    var navigation = context.Services.GetRequiredService<NavigationManager>();

    var first = context.Render<MessagesPage>();
    first.WaitForAssertion(() => Assert.Equal(2, first.FindAll("li").Count));
    await WaitAsync(() => navigation.Uri.EndsWith($"/messages/{Newest}"));
    // The router shows it in the same page; nothing is marked read by
    // arriving.
    var page = first;
    page.Render(x => x.Add(p => p.Id, Newest));
    page.WaitForAssertion(() => Assert.Contains(Newest, api.ThreadReads));
    page.Settle();
    Assert.Empty(api.Reads);

    // Picked in the list, it is read as it is shown.
    await page.Find($"a[href='/messages/{Newest}']")
      .ClickAsync(new() { Button = 0 });
    page.WaitForAssertion(() => Assert.Equal([Newest], api.Reads));
  }

  [Fact]
  public async Task TheConversationLeftOpenIsOpenedAgain()
  {
    var api = new Api();
    await using var context = Context(api, beside: true);
    var navigation = context.Services.GetRequiredService<NavigationManager>();
    // Shown by a link earlier in this tab: read, and remembered.
    var shown = context.Render<MessagesPage>(x => x.Add(p => p.Id, Older));
    shown.WaitForAssertion(() => Assert.Equal([Older], api.Reads));
    shown.Dispose();

    context.Render<MessagesPage>();

    await WaitAsync(() => navigation.Uri.EndsWith($"/messages/{Older}"));
  }

  [Fact]
  public async Task APhoneKeepsItsListAndALinkOpensItsOwnConversation()
  {
    var api = new Api();
    await using var context = Context(api, beside: false);
    var navigation = context.Services.GetRequiredService<NavigationManager>();
    var start = navigation.Uri;

    var page = context.Render<MessagesPage>();
    page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("li").Count));
    page.Settle();
    Assert.Equal(start, navigation.Uri);
    Assert.Contains("Choose a conversation", page.Markup);
    Assert.Empty(api.Reads);
  }

  [Fact]
  public async Task AnotherUsersTabForgetsTheConversation()
  {
    await using var context = Context(new Api(), beside: true);
    var places = context.Services.GetRequiredService<ReturnPlaces>();
    places.LastConversation = Older;

    context.Authorization.SetNotAuthorized();

    Assert.Null(places.LastConversation);
  }

  private static async Task WaitAsync(Func<bool> condition)
  {
    for (var i = 0; i < 200 && !condition(); i++)
      await Task.Delay(10);
    Assert.True(condition());
  }

  private static ClientComponentContext Context(Api api, bool beside)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.JSInterop.SetupModule("./js/generated/shared/messagingChannel.js");
    context.JSInterop.SetupModule("./js/generated/shared/messagingNotices.js");
    var composer = context.JSInterop.SetupModule(
      "./js/generated/messages/composer.js"
    );
    composer.Mode = JSRuntimeMode.Loose;
    composer.Setup<bool>("threadBesideList", _ => true).SetResult(beside);
    context.Services.AddSingleton<TokenStorageService>();
    context.Services.AddSingleton<MessagingSignals>();
    context.Authorization.SetAuthorized("dispatcher");
    return context;
  }

  private sealed class Api
  {
    public List<Guid> Reads { get; } = [];
    public List<Guid> ThreadReads { get; } = [];

    private static ConversationSummary Summary(Guid id, DateTime last) =>
      new(
        id,
        "+15558234327",
        Guid.NewGuid(),
        id == Newest ? "Newest Driver" : "Older Driver",
        "Where do I fuel?",
        last,
        last,
        true,
        1,
        null,
        null,
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
        // The list's own order is not assumed: the newest is second.
        return Ok(
          new InboxView(
            [Summary(Older, Now.AddHours(-2)), Summary(Newest, Now)],
            false
          )
        );
      foreach (var id in new[] { Older, Newest })
      {
        if (path.EndsWith($"/{id}/read"))
        {
          await request.Content!.ReadFromJsonAsync<ReadRequest>(ct);
          Reads.Add(id);
          return Ok(true);
        }
        if (
          path == $"/api/messaging/conversations/{id}"
          && request.Method == HttpMethod.Get
        )
        {
          ThreadReads.Add(id);
          return Ok(
            new ConversationView(
              Summary(id, Now),
              [
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
            {
              ReadThrough = 3,
            }
          );
        }
      }
      return Ok<object?>(null);
    }

    private static HttpResponseMessage Ok<T>(T value) =>
      new(System.Net.HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<T> { Success = true, Response = value }
        ),
      };
  }
}
