using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Bunit;
using Bunit.Rendering;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Services;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// A conversation's messages show as soon as the conversation is read: the
// list, the templates, the trip and the read marker each load beside it and
// none holds the history back. Answers that land late, or for a
// conversation no longer open, change nothing; a poll that finds the open
// conversation unchanged reads nothing more; and a reply or file sent from
// one conversation never clears or blocks another's.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class MessagesLoadingTests
{
  private static readonly Guid A = Guid.NewGuid();
  private static readonly Guid B = Guid.NewGuid();
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
  public async Task HistoryShowsWhileEverythingElseIsStillOnItsWay()
  {
    var api = new Api();
    foreach (var slow in Slow)
      api.Hold(slow);
    await using var context = Context(api);

    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));

    page.WaitForAssertion(() => Assert.Contains("hello from A r", page.Markup));
    page.WaitForAssertion(
      () => Assert.All(Slow, slow => Assert.Equal(1, api.Count(slow)))
    );
    Assert.All(Slow, slow => Assert.Equal(0, api.Answered(slow)));
    Assert.Empty(page.FindAll(".messages__conversation"));

    api.ReleaseAll();
    page.WaitForAssertion(
      () => Assert.Single(page.FindAll(".messages__conversation"))
    );
    Assert.Contains("hello from A r", page.Markup);
    Assert.Equal(1, api.Count($"/api/messaging/conversations/{A}"));
  }

  // B is chosen while A is still being read, then A again while B is; the
  // answers come back in the other order. Only the open conversation's
  // messages show; each opening reads one trip, and A's second opening
  // joins its first read, with one more read after it.
  [Fact]
  public async Task SwitchingBackAndForthShowsOnlyTheOpenConversation()
  {
    var api = new Api();
    var first = api.Hold($"/api/messaging/conversations/{A}");
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));

    var toB = api.Hold($"/api/messaging/conversations/{B}");
    page.Render(x => x.Add(p => p.Id, B));
    page.Render(x => x.Add(p => p.Id, A));
    page.WaitForAssertion(
      () => Assert.Equal(1, api.Count($"/api/messaging/conversations/{B}"))
    );
    // Opening A again while its read is on its way adds no read beside it;
    // one more follows it.
    Assert.Equal(1, api.Count($"/api/messaging/conversations/{A}"));
    toB.SetResult();
    first.SetResult();

    page.WaitForAssertion(() => Assert.Contains("hello from A r", page.Markup));
    Assert.DoesNotContain("hello from B r", page.Markup);
    Assert.Equal(
      (2, 1, 2, 1),
      (
        api.Count($"/api/messaging/conversations/{A}"),
        api.Count($"/api/messaging/conversations/{B}"),
        api.Count($"/api/messaging/conversations/{A}/context"),
        api.Count($"/api/messaging/conversations/{B}/context")
      )
    );
  }

  [Fact]
  public async Task APollReadsTheOpenConversationOnlyWhenItChanged()
  {
    var api = new Api { Unread = 0 };
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    page.WaitForAssertion(
      () => Assert.Single(page.FindAll(".messages__conversation"))
    );
    var signals = context.Services.GetRequiredService<MessagingSignals>();
    var inbox = api.Count("/api/messaging/inbox");

    signals.Receive("poll", null);
    page.WaitForAssertion(
      () => Assert.Equal(inbox + 1, api.Answered("/api/messaging/inbox"))
    );
    Assert.Equal(1, api.Count($"/api/messaging/conversations/{A}"));

    api.Revision = 4;
    signals.Receive("poll", null);
    page.WaitForAssertion(
      () => Assert.Equal(2, api.Count($"/api/messaging/conversations/{A}"))
    );
    Assert.Equal(0, api.Count($"/api/messaging/conversations/{A}/read"));
  }

  // A change signal rereads the open conversation, and that read is held
  // with the revision it began at. A poll then finds a newer revision: the
  // demand is kept, and the conversation is read once more after the held
  // read lands, not dropped because a read was already on its way.
  [Fact]
  public async Task ADemandDuringARereadIsReadAfterIt()
  {
    var api = new Api { Unread = 0 };
    await using var context = Context(api);
    api.ArmReread();
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    Settle(page, api);
    var signals = context.Services.GetRequiredService<MessagingSignals>();
    var thread = $"/api/messaging/conversations/{A}";
    var held = api.Hold(thread);
    var inbox = api.Answered("/api/messaging/inbox");
    signals.Receive("change", A.ToString());
    api.WaitForRequests(thread, 2);

    api.Revision = 4;
    signals.Receive("poll", null);
    page.WaitForAssertion(
      () => Assert.True(api.Answered("/api/messaging/inbox") >= inbox + 2)
    );
    Assert.Equal(2, api.Count(thread));
    held.SetResult();

    page.WaitForAssertion(
      () => Assert.Contains("hello from A r4", page.Markup)
    );
    Assert.Equal(3, api.Count(thread));
  }

  // A's reply is on its way; B is opened and a reply sent there, then A
  // again. A stays busy until its own reply answers, so it cannot be sent
  // twice, and that answer, coming to a later opening of A, clears
  // nothing there; A is read again to show it.
  [Fact]
  public async Task OverlappingRepliesAcrossReopeningAreSentOnceEach()
  {
    var api = new Api();
    var sentA = api.Hold($"/api/messaging/conversations/{A}/messages");
    await using var context = Context(api);
    api.ArmReread();
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    Settle(page, api);
    page.Find("#messages-text").Input("to A");
    var sendingA = page.Find(".messages__composer").SubmitAsync();

    api.ArmReread();
    page.Render(x => x.Add(p => p.Id, B));
    Settle(page, api);
    page.Find("#messages-text").Input("to B");
    await page.Find(".messages__composer").SubmitAsync();
    api.ArmReread();
    page.Render(x => x.Add(p => p.Id, A));
    Settle(page, api);

    Assert.True(page.Find("#messages-text").HasAttribute("disabled"));
    await page.Find(".messages__composer").SubmitAsync();
    var threadReads = api.Count($"/api/messaging/conversations/{A}");
    sentA.SetResult();
    await sendingA;

    page.WaitForAssertion(
      () =>
        Assert.True(
          api.Count($"/api/messaging/conversations/{A}") > threadReads
        )
    );
    Assert.Equal(
      (1, 1),
      (
        api.Count($"/api/messaging/conversations/{A}/messages"),
        api.Count($"/api/messaging/conversations/{B}/messages")
      )
    );
    page.WaitForAssertion(
      () => Assert.False(page.Find("#messages-text").HasAttribute("disabled"))
    );
  }

  // The page acknowledges a read and then reads the list again; that answer
  // renders the page once more and replaces the composer's input handler.
  // Typing into the box found before it and dispatched after it reaches a
  // handler that is gone (UnknownEventHandlerIdException, seen in the gate)
  // - which is why Settle waits for this answer to be on screen.
  [Fact]
  public async Task TheListReadAfterAReadRendersTheComposerAgain()
  {
    var api = new Api();
    api.ArmReread();
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    var reread = api.TakeReread()!;
    reread.WaitAsked();
    var before = page.Find("#messages-text").GetAttribute("blazor:oninput");

    reread.Release(
      page,
      () => Assert.True(ListShown(page) >= api.RereadNumber)
    );

    Assert.NotNull(before);
    Assert.NotEqual(
      before,
      page.Find("#messages-text").GetAttribute("blazor:oninput")
    );
    Assert.Equal(1, api.Answered($"/api/messaging/conversations/{A}/read"));
  }

  // Changes to five other conversations arrive while the list is being
  // read: one more read serves them all.
  [Fact]
  public async Task ABurstOfSignalsReadsTheListTwice()
  {
    var api = new Api();
    await using var context = Context(api);
    api.ArmReread();
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    Settle(page, api);
    var signals = context.Services.GetRequiredService<MessagingSignals>();
    var before = api.Count("/api/messaging/inbox");
    var held = api.Hold("/api/messaging/inbox");

    for (var i = 0; i < 5; i++)
      signals.Receive("change", Guid.NewGuid().ToString());
    api.WaitForRequests("/api/messaging/inbox", before + 1);
    held.SetResult();

    page.WaitForAssertion(
      () => Assert.Equal(before + 2, api.Answered("/api/messaging/inbox"))
    );
    page.Settle();
    Assert.Equal(before + 2, api.Count("/api/messaging/inbox"));
    Assert.Equal(1, api.Count($"/api/messaging/conversations/{A}"));
  }

  // Six files chosen together stage five; and what waits to be sent stays
  // within the total the browser holds.
  [Fact]
  public async Task StagingKeepsToItsCountAndSize()
  {
    var api = new Api();
    await using var context = Context(api);
    api.ArmReread();
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    Settle(page, api);

    page.FindComponent<InputFile>()
      .UploadFiles(
        [
          .. Enumerable
            .Range(0, 6)
            .Select(i =>
              InputFileContent.CreateFromBinary(
                [1],
                $"f{i}.pdf",
                contentType: "application/pdf"
              )
            ),
        ]
      );
    page.WaitForAssertion(
      () => Assert.Contains("Send at most 5 files", page.Markup)
    );
    Assert.Equal(5, page.FindAll(".messages__staged-file").Count);
    foreach (var name in new[] { "f1", "f2", "f3", "f4" })
      await ClickAsync(page, $"[aria-label='Remove {name}.pdf']");

    var large = new byte[12 * 1024 * 1024];
    page.FindComponent<InputFile>()
      .UploadFiles(
        [
          .. Enumerable
            .Range(0, 3)
            .Select(i =>
              InputFileContent.CreateFromBinary(
                large,
                $"big{i}.pdf",
                contentType: "application/pdf"
              )
            ),
        ]
      );
    page.WaitForAssertion(
      () => Assert.Contains("total at most 32 MB", page.Markup)
    );
    Assert.Equal(
      ["f0.pdf", "big0.pdf", "big1.pdf"],
      page.FindAll(".messages__staged-name").Select(x => x.TextContent)
    );
    Assert.Empty(api.Uploads);
  }

  [Fact]
  public async Task AReplyAnsweredAfterTheSwitchLeavesTheOtherDraftAlone()
  {
    var api = new Api();
    var sent = api.Hold($"/api/messaging/conversations/{A}/messages");
    await using var context = Context(api);
    api.ArmReread();
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    Settle(page, api);
    page.Find("#messages-text").Input("to A");
    var sending = page.Find(".messages__composer").SubmitAsync();

    api.ArmReread();
    page.Render(x => x.Add(p => p.Id, B));
    page.WaitForAssertion(() => Assert.Contains("hello from B r", page.Markup));
    Settle(page, api);
    page.Find("#messages-text").Input("to B");
    Assert.False(page.Find("#messages-text").HasAttribute("disabled"));
    sent.SetResult();
    await sending;

    Assert.Equal("to B", page.Find("#messages-text").GetAttribute("value"));
    Assert.Equal(1, api.Count($"/api/messaging/conversations/{B}"));
  }

  // Two files wait under the box with the text; one is taken off. Send
  // uploads the other with the text as its caption. Its first attempt
  // fails; Send again repeats it with the same retry key and caption.
  [Fact]
  public async Task StagedFilesWaitForSendAndARetryKeepsTheirKey()
  {
    var api = new Api { FailFirstFile = true };
    await using var context = Context(api);
    api.ArmReread();
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    Settle(page, api);

    page.FindComponent<InputFile>()
      .UploadFiles(
        InputFileContent.CreateFromBinary(
          [1, 2, 3],
          "bol.pdf",
          contentType: "application/pdf"
        ),
        InputFileContent.CreateFromBinary(
          [4, 5],
          "extra.pdf",
          contentType: "application/pdf"
        )
      );
    page.WaitForAssertion(
      () => Assert.Equal(2, page.FindAll(".messages__staged-file").Count)
    );
    Assert.Empty(api.Uploads);
    await ClickAsync(page, "[aria-label='Remove extra.pdf']");
    page.WaitForAssertion(
      () => Assert.Single(page.FindAll(".messages__staged-file"))
    );
    page.Find("#messages-text").Input("Signed BOL");
    await page.Find(".messages__composer").SubmitAsync();

    page.WaitForAssertion(() => Assert.Contains("Send again", page.Markup));
    await ClickAsync(page, ".messages__staged-file .btn--text");

    page.WaitForAssertion(
      () => Assert.Empty(page.FindAll(".messages__staged-file"))
    );
    Assert.Equal(2, api.Uploads.Count);
    Assert.All(api.Uploads, x => Assert.Equal("bol.pdf", x.Name));
    Assert.Equal(api.Uploads[0].Key, api.Uploads[1].Key);
    Assert.All(api.Uploads, x => Assert.Equal("Signed BOL", x.Caption));
    Assert.Equal("", page.Find("#messages-text").GetAttribute("value") ?? "");
  }

  // A file's staging renders again when its reading completes, which can
  // replace a control between finding and clicking it: find it again.
  private static async Task ClickAsync(
    IRenderedComponent<MessagesPage> page,
    string selector
  )
  {
    for (var attempt = 0; ; attempt++)
      try
      {
        page.Settle();
        await page.Find(selector).ClickAsync(new());
        return;
      }
      catch (UnknownEventHandlerIdException) when (attempt < 20) { }
  }

  // The thread, the list and the trip have all landed, and so has the
  // page's own follow-up: with unread messages it acknowledges the read,
  // then reads the list again, and that answer renders the page once more,
  // replacing the composer's handlers
  // (TheListReadAfterAReadRendersTheComposerAgain). It is held until asked,
  // then released, and the test waits until the list on screen is that
  // answer or a later one - not merely for some render. After it nothing
  // more is on its way, so a control found then stays.
  private static void Settle(IRenderedComponent<MessagesPage> page, Api api)
  {
    page.WaitForAssertion(() =>
    {
      Assert.Single(page.FindAll(".messages__conversation"));
      Assert.NotNull(page.Find("#messages-text"));
    });
    if (api.TakeReread() is { } reread)
      reread.Release(
        page,
        () => Assert.True(ListShown(page) >= api.RereadNumber)
      );
    page.WaitForAssertion(
      () => Assert.Empty(page.FindAll("[role=status]:not(.visually-hidden)"))
    );
  }

  // Which list answer the page shows: each carries its number.
  private static int ListShown(IRenderedComponent<MessagesPage> page) =>
    page.FindAll(".messages__conversation")
      .Select(row => Regex.Match(row.TextContent, @"\[list (\d+)\]"))
      .Where(match => match.Success)
      .Select(match => int.Parse(match.Groups[1].Value))
      .DefaultIfEmpty(0)
      .Max();

  private static readonly string[] Slow =
  [
    "/api/messaging/inbox",
    "/api/messaging/templates",
    $"/api/messaging/conversations/{A}/context",
    $"/api/messaging/conversations/{A}/read",
  ];

  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    // The channel joins, so no stream reader polls in the background: every
    // signal here is the test's own.
    context.JSInterop.SetupModule("./js/generated/shared/messagingChannel.js");
    context.JSInterop.SetupModule("./js/generated/shared/messagingNotices.js");
    return context;
  }

  private sealed class Api
  {
    private readonly ConcurrentDictionary<string, int> _requests = [];
    private readonly ConcurrentDictionary<string, int> _answered = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _held =
    [];

    public int Unread { get; init; } = 1;
    public long Revision { get; set; } = 3;
    public bool FailFirstFile { get; init; }
    public List<(string Name, string Key, string Caption)> Uploads { get; } =
      [];

    public TaskCompletionSource Hold(string path) =>
      _held[path] = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ReleaseAll()
    {
      foreach (var held in _held.Values)
        held.TrySetResult();
    }

    // The list read that follows a read acknowledgment, held once when
    // armed (see Settle). With nothing unread there is no read to follow.
    private readonly Lock _reread = new();
    private HeldRequest? _armed;
    private HeldRequest? _heldReread;
    private bool _readAnswered;
    private int _lists;

    // The number the held list answer carries.
    public int RereadNumber { get; private set; }

    public void ArmReread()
    {
      lock (_reread)
      {
        _armed = Unread > 0 ? new HeldRequest() : null;
        _heldReread = _armed;
        _readAnswered = false;
      }
    }

    public HeldRequest? TakeReread()
    {
      lock (_reread)
      {
        var held = _heldReread;
        _heldReread = null;
        return held;
      }
    }

    private HeldRequest? HoldReread(string path, int number)
    {
      lock (_reread)
      {
        if (_armed is not { } armed || path != "/api/messaging/inbox")
          return null;
        if (!_readAnswered)
          return null;
        _armed = null;
        RereadNumber = number;
        return armed;
      }
    }

    private void NoteAnswered(string path)
    {
      if (!path.EndsWith("/read", StringComparison.Ordinal))
        return;
      lock (_reread)
        _readAnswered = _armed is not null;
    }

    public int Count(string path) => _requests.GetValueOrDefault(path);

    // A held request renders nothing, and bUnit re-checks a waited-for
    // assertion only when the page renders, so the count itself is watched.
    public void WaitForRequests(string path, int count) =>
      Assert.True(
        SpinWait.SpinUntil(
          () => Count(path) >= count,
          BunitContext.DefaultWaitTimeout
        ),
        $"{path} was asked {Count(path)} times, not {count}."
      );

    public int Answered(string path) => _answered.GetValueOrDefault(path);

    private ConversationSummary Summary(Guid id) => Summary(id, Revision);

    private ConversationSummary Summary(Guid id, long revision) =>
      new(
        id,
        "+15550000000",
        null,
        id == A ? "Ann A" : "Bo B",
        "hello",
        Now,
        Now,
        true,
        Unread,
        null,
        null,
        revision
      );

    // As the server reads it: at the revision current when the request
    // arrived, however late the answer comes.
    private ConversationView Thread(Guid id, long revision) =>
      new(
        Summary(id, revision),
        [
          new(
            Guid.NewGuid(),
            "in",
            "text",
            (id == A ? "hello from A" : "hello from B") + $" r{revision}",
            "received",
            Now,
            null,
            null,
            []
          ),
        ],
        false
      );

    public async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      var revision = Revision;
      _requests.AddOrUpdate(path, 1, (_, n) => n + 1);
      var list =
        path == "/api/messaging/inbox" ? Interlocked.Increment(ref _lists) : 0;
      if (_held.TryGetValue(path, out var held))
      {
        await held.Task;
        // A held answer is given once; the next request answers at once.
        _held.TryRemove(path, out _);
      }
      if (HoldReread(path, list) is { } reread)
        await reread.HoldAsync();
      _answered.AddOrUpdate(path, 1, (_, n) => n + 1);
      NoteAnswered(path);
      if (path == "/api/messaging/templates")
        return Ok<IReadOnlyList<MessageTemplateView>>([]);
      if (path == "/api/messaging/inbox")
        return Ok(
          new InboxView(
            [Summary(A) with { LastPreview = $"hello [list {list}]" }],
            false
          )
        );
      foreach (var id in new[] { A, B })
      {
        var at = $"/api/messaging/conversations/{id}";
        if (path == at && request.Method == HttpMethod.Get)
          return Ok(Thread(id, revision));
        if (path == $"{at}/context")
          return Ok(
            new ConversationContext(null, null, "unknown", [], [], null)
          );
        if (path == $"{at}/read")
          return Ok(true);
        if (path == $"{at}/messages")
          return Ok(
            new MessageView(
              Guid.NewGuid(),
              "out",
              "text",
              "sent",
              "queued",
              Now,
              null,
              null,
              []
            )
          );
        if (path == $"{at}/files")
          return await UploadAsync(request, ct);
      }
      return new(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> UploadAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var form = (MultipartFormDataContent)request.Content!;
      string Field(string name) =>
        form.First(x => x.Headers.ContentDisposition!.Name == name)
          .ReadAsStringAsync(ct)
          .Result;
      var file = form.First(x => x.Headers.ContentDisposition!.Name == "file");
      Uploads.Add(
        (
          file.Headers.ContentDisposition!.FileName!.Trim('"'),
          Field("idempotencyKey"),
          Field("caption")
        )
      );
      await Task.Yield();
      if (FailFirstFile && Uploads.Count == 1)
        return new(HttpStatusCode.BadGateway)
        {
          Content = JsonContent.Create(
            new RequestResponseDTO<MessageView> { Success = false }
          ),
        };
      return Ok(
        new MessageView(
          Guid.NewGuid(),
          "out",
          "file",
          "",
          "queued",
          Now,
          null,
          null,
          []
        )
      );
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
