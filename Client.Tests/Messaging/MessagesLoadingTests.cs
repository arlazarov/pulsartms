using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
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
  // messages show, and each opening reads one thread and one trip.
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
      () => Assert.Equal(2, api.Count($"/api/messaging/conversations/{A}"))
    );
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
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    Settle(page);
    var signals = context.Services.GetRequiredService<MessagingSignals>();
    var thread = $"/api/messaging/conversations/{A}";
    var held = api.Hold(thread);
    var inbox = api.Answered("/api/messaging/inbox");
    signals.Receive("change", A.ToString());
    page.WaitForAssertion(() => Assert.Equal(2, api.Count(thread)));

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

  [Fact]
  public async Task AReplyAnsweredAfterTheSwitchLeavesTheOtherDraftAlone()
  {
    var api = new Api();
    var sent = api.Hold($"/api/messaging/conversations/{A}/messages");
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    Settle(page);
    page.Find("#messages-text").Input("to A");
    var sending = page.Find(".messages__composer").SubmitAsync();

    page.Render(x => x.Add(p => p.Id, B));
    page.WaitForAssertion(() => Assert.Contains("hello from B r", page.Markup));
    Settle(page);
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
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    Settle(page);

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
        await page.Find(selector).ClickAsync(new());
        return;
      }
      catch (UnknownEventHandlerIdException) when (attempt < 5)
      {
        await Task.Delay(20);
      }
  }

  // The thread, the list and the trip have all landed, so a control found
  // now is the one that stays on screen.
  private static void Settle(IRenderedComponent<MessagesPage> page)
  {
    page.WaitForAssertion(() =>
    {
      Assert.Single(page.FindAll(".messages__conversation"));
      Assert.NotNull(page.Find("#messages-text"));
      Assert.Empty(page.FindAll("[role=status]"));
    });
    page.Settle();
  }

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

    public int Count(string path) => _requests.GetValueOrDefault(path);

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
      if (_held.TryGetValue(path, out var held))
      {
        await held.Task;
        // A held answer is given once; the next request answers at once.
        _held.TryRemove(path, out _);
      }
      _answered.AddOrUpdate(path, 1, (_, n) => n + 1);
      if (path == "/api/messaging/templates")
        return Ok<IReadOnlyList<MessageTemplateView>>([]);
      if (path == "/api/messaging/inbox")
        return Ok(new InboxView([Summary(A)], false));
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
