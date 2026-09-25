using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Web;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// A long conversation loads fifty messages at a time as the dispatcher
// comes near the top, one page at a time, and never all at once. A reread
// of the newest page keeps the older pages shown and continues from where
// they end; a page for a conversation no longer open is dropped; a page
// that fails can be asked for again.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class MessagesHistoryTests
{
  private static readonly Guid A = Guid.NewGuid();
  private static readonly Guid B = Guid.NewGuid();

  [Fact]
  public async Task AThousandMessagesLoadOnlyAsTheDispatcherScrollsUp()
  {
    var api = new Api(1030);
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    page.WaitForAssertion(() => Assert.Equal(50, Shown(page).Count));

    var held = api.HoldOlder();
    await Near(page);
    await Near(page);
    page.WaitForAssertion(() => Assert.Equal(1, api.OlderReads));
    Assert.NotEmpty(page.FindAll(".messages__earlier[role=status]"));
    held.SetResult();
    page.WaitForAssertion(() => Assert.Equal(100, Shown(page).Count));

    while (Shown(page).Count < 1030)
    {
      var count = Shown(page).Count;
      await Near(page);
      page.WaitForAssertion(() => Assert.True(Shown(page).Count > count));
    }

    Assert.Equal(api.OldestFirst(A), Shown(page));
    Assert.Equal(20, api.OlderReads);
    Assert.Contains("Start of the conversation", page.Markup);
    await Near(page);
    page.Settle();
    Assert.Equal(20, api.OlderReads);
  }

  [Fact]
  public async Task ARereadKeepsTheHistoryShownAndContinuesBelowIt()
  {
    var api = new Api(400);
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    page.WaitForAssertion(() => Assert.Equal(50, Shown(page).Count));
    await Near(page);
    page.WaitForAssertion(() => Assert.Equal(100, Shown(page).Count));
    await Near(page);
    page.WaitForAssertion(() => Assert.Equal(150, Shown(page).Count));

    var arrived = api.Arrive(A);
    context
      .Services.GetRequiredService<MessagingSignals>()
      .Receive("change", A.ToString());
    page.WaitForAssertion(() => Assert.Equal(151, Shown(page).Count));
    Assert.Equal(arrived, Shown(page)[^1]);
    Assert.Equal(api.OldestFirst(A)[^151..], Shown(page));

    var oldest = Shown(page)[0];
    await Near(page);
    page.WaitForAssertion(() => Assert.Equal(201, Shown(page).Count));
    Assert.Equal(oldest.ToString(), api.LastBefore);
    Assert.Equal(api.OldestFirst(A)[^201..], Shown(page));
  }

  [Fact]
  public async Task AnOlderPageAnsweredAfterTheSwitchIsDropped()
  {
    var api = new Api(120);
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    page.WaitForAssertion(() => Assert.Equal(50, Shown(page).Count));
    var held = api.HoldOlder();
    await Near(page);
    page.WaitForAssertion(() => Assert.Equal(1, api.OlderReads));

    page.Render(x => x.Add(p => p.Id, B));
    page.WaitForAssertion(
      () => Assert.Equal(api.OldestFirst(B)[^50..], Shown(page))
    );
    held.SetResult();
    page.Settle();

    Assert.Equal(api.OldestFirst(B)[^50..], Shown(page));
  }

  [Fact]
  public async Task AFailedOlderPageCanBeAskedForAgain()
  {
    var api = new Api(120) { FailOlder = 1 };
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    page.WaitForAssertion(() => Assert.Equal(50, Shown(page).Count));

    await Near(page);
    page.WaitForAssertion(
      () => Assert.Contains("could not be loaded", page.Markup)
    );
    // Coming near the top again does not ask on its own after a failure.
    await Near(page);
    page.Settle();
    Assert.Equal(1, api.OlderReads);
    await page.FindAll(".messages__earlier button")
      .Single(x => x.TextContent.Trim() == "Try again")
      .ClickAsync(new());

    page.WaitForAssertion(() => Assert.Equal(100, Shown(page).Count));
  }

  private static Task Near(IRenderedComponent<MessagesPage> page) =>
    page.InvokeAsync(() => page.Instance.NearOldest());

  private static List<Guid> Shown(IRenderedComponent<MessagesPage> page) =>
    [
      .. page.FindAll(".messages__item[data-message-id]")
        .Select(x => Guid.Parse(x.GetAttribute("data-message-id")!)),
    ];

  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.JSInterop.SetupModule("./js/generated/shared/messagingChannel.js");
    context.JSInterop.SetupModule("./js/generated/shared/messagingNotices.js");
    return context;
  }

  // Each conversation's messages in the server's order - newest first by
  // time, then when recorded, then id - with seven to each second, and
  // pages below a cursor exactly as the server cuts them.
  private sealed class Api
  {
    private static readonly DateTime Start = new(
      2026,
      9,
      1,
      12,
      0,
      0,
      DateTimeKind.Utc
    );
    private readonly ConcurrentDictionary<Guid, List<Stored>> _threads = [];
    private TaskCompletionSource? _older;
    private int _olderReads;

    public int FailOlder { get; set; }
    public int OlderReads => _olderReads;
    public string? LastBefore { get; private set; }

    private sealed record Stored(Guid Id, DateTime SentAt, DateTime CreatedAt);

    public Api(int count)
    {
      foreach (var id in new[] { A, B })
        _threads[id] =
        [
          .. Enumerable
            .Range(0, count)
            .Select(i => new Stored(
              Guid.NewGuid(),
              Start.AddSeconds(i / 7),
              Start.AddSeconds(i / 7)
            )),
        ];
    }

    public TaskCompletionSource HoldOlder() =>
      _older = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Guid Arrive(Guid conversation)
    {
      var added = new Stored(
        Guid.NewGuid(),
        Start.AddDays(1),
        Start.AddDays(1)
      );
      _threads[conversation].Add(added);
      return added.Id;
    }

    private List<Stored> NewestFirst(Guid id) =>
      [
        .. _threads[id]
          .OrderByDescending(x => x.SentAt)
          .ThenByDescending(x => x.CreatedAt)
          .ThenByDescending(x => x.Id),
      ];

    public List<Guid> OldestFirst(Guid id) =>
      [.. NewestFirst(id).Select(x => x.Id).Reverse()];

    public async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      if (path == "/api/messaging/templates")
        return Ok<IReadOnlyList<MessageTemplateView>>([]);
      if (path == "/api/messaging/inbox")
        return Ok(new InboxView([], false));
      foreach (var id in new[] { A, B })
        if (path == $"/api/messaging/conversations/{id}")
          return await PageAsync(id, request.RequestUri.Query);
      return new(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> PageAsync(Guid id, string query)
    {
      var all = NewestFirst(id);
      var below = HttpUtility.ParseQueryString(query)["beforeId"];
      if (below is not null)
      {
        Interlocked.Increment(ref _olderReads);
        LastBefore = below;
        if (_older is { } held)
        {
          _older = null;
          await held.Task;
        }
        if (FailOlder > 0)
        {
          FailOlder--;
          return new(HttpStatusCode.ServiceUnavailable);
        }
        all = [.. all.SkipWhile(x => x.Id != Guid.Parse(below)).Skip(1)];
      }
      var page = all.Take(50).ToList();
      var view = new ConversationView(
        new(
          id,
          "+15550000000",
          null,
          id == A ? "Ann A" : "Bo B",
          "",
          Start,
          Start,
          true,
          0,
          null,
          null,
          _threads[id].Count
        ),
        [
          .. page.Select(x => new MessageView(
            x.Id,
            "in",
            "text",
            $"m {x.Id}",
            "received",
            x.SentAt,
            null,
            null,
            []
          )),
        ],
        all.Count > 50
      )
      {
        Next =
          all.Count > 50
            ? new(page[^1].SentAt, page[^1].CreatedAt, page[^1].Id)
            : null,
      };
      return Ok(view);
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
