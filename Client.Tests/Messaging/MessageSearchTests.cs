using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Pages.Messages;
using Client.Services;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// Search asks the server with every filter and the browser's time zone,
// keeps only the newest search's answer, and a result opens its
// conversation around it: nothing is marked read there, a change that
// arrives keeps the window and offers the newest, and Jump to newest
// leaves it.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class MessageSearchTests
{
  private static readonly Guid A = Guid.NewGuid();
  private static readonly Guid Target = Guid.NewGuid();
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
  public async Task TheNewestSearchWinsAndCarriesEveryFilter()
  {
    var api = new Api();
    var first = api.HoldSearch();
    await using var context = Context(api);
    var search = context.Render<MessageSearch>(x =>
      x.Add(p => p.ConversationId, A)
    );

    search.Find("input[type=search]").Input("tarp");
    await Eventually(() => Assert.Single(api.Searches));
    search.Find("input[aria-label='Load number']").Input("1407");
    search.Find("input[aria-label='From day']").Change("2026-09-01");
    // Both edits fall within one wait: one more search, not two.
    await Eventually(() => Assert.Equal(2, api.Searches.Count));
    first.SetResult();

    search.WaitForAssertion(() => Assert.Contains("load=1407", search.Markup));
    search.Settle();
    Assert.Single(search.FindAll(".messages__results li"));
    var last = api.Searches[^1];
    Assert.Contains($"conversationId={A}", last);
    Assert.Contains("text=tarp", last);
    Assert.Contains("load=1407", last);
    Assert.Contains("from=2026-09-01", last);
    Assert.Contains(
      $"timeZone={Uri.EscapeDataString(TimeZoneInfo.Local.Id)}",
      last
    );
  }

  [Fact]
  public async Task AResultOpensAWindowThatMarksNothingReadAndKeepsItsPlace()
  {
    var api = new Api();
    await using var context = Context(api);
    var navigation = context.Services.GetRequiredService<NavigationManager>();
    navigation.NavigateTo($"/messages/{A}?around={Target}");
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));

    page.WaitForAssertion(
      () => Assert.Contains("around a search result", page.Markup)
    );
    page.WaitForAssertion(() => Assert.Contains("the result", page.Markup));
    page.Settle();
    Assert.Equal(1, api.Count("around"));
    Assert.Equal(0, api.Count("read"));

    context
      .Services.GetRequiredService<MessagingSignals>()
      .Receive("change", A.ToString());
    page.WaitForAssertion(() => Assert.Equal(1, api.Count("newest")));
    page.Settle();
    Assert.Contains("the result", page.Markup);
    Assert.Contains("New messages", page.Markup);
    Assert.Equal(0, api.Count("read"));

    await page.FindAll("button")
      .Single(x => x.TextContent.Trim() == "Jump to newest")
      .ClickAsync(new());
    Assert.EndsWith($"/messages/{A}", navigation.Uri);
  }

  // The requests are counted after the page last rendered.
  private static async Task Eventually(Action assertion)
  {
    for (var attempt = 0; ; attempt++)
      try
      {
        assertion();
        return;
      }
      catch (Exception) when (attempt < 200)
      {
        await Task.Delay(10);
      }
  }

  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.JSInterop.SetupModule("./js/generated/shared/messagingChannel.js");
    context.JSInterop.SetupModule("./js/generated/shared/messagingNotices.js");
    return context;
  }

  private sealed class Api
  {
    private readonly ConcurrentDictionary<string, int> _counts = [];
    private TaskCompletionSource? _held;

    public List<string> Searches { get; } = [];

    public TaskCompletionSource HoldSearch() =>
      _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Count(string kind) => _counts.GetValueOrDefault(kind);

    private void Add(string kind) =>
      _counts.AddOrUpdate(kind, 1, (_, n) => n + 1);

    private static ConversationSummary Summary() =>
      new(
        A,
        "+15550000000",
        null,
        "Ann A",
        "",
        Now,
        Now,
        true,
        1,
        null,
        null,
        9
      );

    private static MessageView Message(Guid id, string body, DateTime at) =>
      new(id, "in", "text", body, "received", at, null, null, []);

    public async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      var query = Uri.UnescapeDataString(
        request.RequestUri.Query.TrimStart('?')
      );
      if (path == "/api/messaging/templates")
        return Ok<IReadOnlyList<MessageTemplateView>>([]);
      if (path == "/api/messaging/inbox")
        return Ok(new InboxView([], false));
      if (path == "/api/messaging/search")
      {
        Searches.Add(request.RequestUri.Query);
        if (_held is { } held && Searches.Count == 1)
          await held.Task;
        return Ok(
          new MessageSearchView(
            [
              new(
                Target,
                A,
                "Ann A",
                "+15550000000",
                "in",
                Now,
                $"hit for {query}",
                false,
                false
              ),
            ],
            false
          )
        );
      }
      if (path == $"/api/messaging/conversations/{A}/read")
      {
        Add("read");
        return Ok(true);
      }
      if (path == $"/api/messaging/conversations/{A}")
      {
        if (query.Contains("around"))
        {
          Add("around");
          return Ok(
            new ConversationView(
              Summary(),
              [Message(Target, "the result", Now.AddDays(-30))],
              true
            )
            {
              Newer = true,
              ReadThrough = 0,
            }
          );
        }
        Add("newest");
        return Ok(
          new ConversationView(
            Summary(),
            [Message(Guid.NewGuid(), "newest", Now)],
            true
          )
          {
            ReadThrough = 9,
          }
        );
      }
      return new(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Ok<T>(T response) =>
      new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<T> { Success = true, Response = response }
        ),
      };
  }
}
