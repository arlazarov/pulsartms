using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Messaging;
using Client.Models.DTO.Mileage;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// A dispatcher opens a driver or filing editor on conversation A, or sends
// its request, then moves to conversation B before it answers. Nothing
// chosen on A shows on B, and A's late answer neither reloads B nor
// opens anything there; A's request still went to A.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class MessageSwitchTests
{
  private static readonly Guid A = Guid.NewGuid();
  private static readonly Guid B = Guid.NewGuid();

  [Fact]
  public async Task AnOpenFilingChoiceOnADoesNotCarryToB()
  {
    var api = new Api();
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    page.WaitForAssertion(() => Assert.Contains("a.pdf", page.Markup));

    await Button(page, "File to load").ClickAsync(new());
    page.WaitForElement("[id^=filing-load]").Change("");
    page.WaitForElement("[id^=filing-number]").Change("1407");
    page.Render(x => x.Add(p => p.Id, B));

    page.WaitForAssertion(() => Assert.Contains("b.pdf", page.Markup));
    Assert.Empty(page.FindAll(".messages__file .messages__filing"));
    Assert.DoesNotContain("1407", page.Markup);
  }

  // The thread is read again while a filing choice is open, and a message
  // now comes before the one being filed. The open choice stays with its
  // own file.
  [Fact]
  public async Task AnOpenFilingChoiceStaysWithItsFileWhenMessagesMove()
  {
    var api = new Api();
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    page.WaitForAssertion(() => Assert.Contains("a.pdf", page.Markup));
    await Button(page, "File to load").ClickAsync(new());
    page.WaitForElement("[id^=filing-load]").Change("");
    page.WaitForElement("[id^=filing-number]").Change("1407");

    api.Earlier = true;
    context
      .Services.GetRequiredService<MessagingSignals>()
      .Receive("change", A.ToString());

    page.WaitForAssertion(() => Assert.Contains("z.pdf", page.Markup));
    var open = Assert.Single(page.FindAll(".messages__file .messages__filing"));
    Assert.Contains("a.pdf", open.Closest(".messages__item")!.TextContent);
  }

  [Fact]
  public async Task AFilingAnsweredAfterTheSwitchDoesNotTouchB()
  {
    var api = new Api { HoldFiling = true };
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    page.WaitForAssertion(() => Assert.Contains("a.pdf", page.Markup));
    await Button(page, "File to load").ClickAsync(new());
    var filing = page.Find(".messages__file .messages__filing").SubmitAsync();

    page.Render(x => x.Add(p => p.Id, B));
    page.WaitForAssertion(() => Assert.Contains("b.pdf", page.Markup));
    api.Filed.SetResult();
    await filing;

    Assert.Equal($"/api/messaging/attachments/{api.FileA}/file", api.Filing);
    Assert.Equal(1, api.Reads(B));
    Assert.Empty(page.FindAll(".messages__filing"));
    Assert.DoesNotContain("could not", page.Markup);
  }

  [Fact]
  public async Task ADriverChosenOnAAndAnsweredAfterTheSwitchDoesNotTouchB()
  {
    var api = new Api { HoldDriver = true };
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    page.WaitForAssertion(() => Assert.Contains("a.pdf", page.Markup));
    await Button(page, "Choose driver").ClickAsync(new());
    page.WaitForElement("[id^=driver-] option[value]");
    page.Find("[id^=driver-]").Change(api.Driver.ToString());
    var saving = page.Find(".messages__context .messages__filing")
      .SubmitAsync();

    page.Render(x => x.Add(p => p.Id, B));
    page.WaitForAssertion(() => Assert.Contains("b.pdf", page.Markup));
    Assert.Empty(page.FindAll("[id^=driver-]"));
    api.Linked.SetResult();
    await saving;

    Assert.Equal($"/api/messaging/conversations/{A}/driver", api.Linking);
    Assert.Equal((1, 1), (api.Reads(B), api.ContextReads(B)));
    Assert.Empty(page.FindAll("[id^=driver-]"));
  }

  // The trip loads beside the thread, so its controls may come after it.
  private static IElement Button(
    IRenderedComponent<MessagesPage> page,
    string text
  )
  {
    page.WaitForAssertion(
      () =>
        Assert.Contains(
          page.FindAll("button"),
          x => x.TextContent.Trim() == text
        )
    );
    return page.FindAll("button").First(x => x.TextContent.Trim() == text);
  }

  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    return context;
  }

  private sealed class Api
  {
    private static readonly DateTime Now = new(
      2026,
      9,
      21,
      12,
      0,
      0,
      DateTimeKind.Utc
    );
    private readonly Dictionary<string, int> _reads = [];

    public bool HoldFiling { get; init; }
    public bool Earlier { get; set; }
    public bool HoldDriver { get; init; }
    public TaskCompletionSource Filed { get; } = new();
    public TaskCompletionSource Linked { get; } = new();
    public Guid FileA { get; } = Guid.NewGuid();
    public Guid FileB { get; } = Guid.NewGuid();
    public Guid Driver { get; } = Guid.NewGuid();
    public string? Filing { get; private set; }
    public string? Linking { get; private set; }

    public int Reads(Guid id) =>
      _reads.GetValueOrDefault($"/api/messaging/conversations/{id}");

    public int ContextReads(Guid id) =>
      _reads.GetValueOrDefault($"/api/messaging/conversations/{id}/context");

    private static ConversationSummary Summary(Guid id) =>
      new(id, "+15550000000", null, null, "", Now, Now, true, 0, null, null, 3);

    // Stable ids, as the server's are: one message with a file in each
    // conversation, and in A an earlier one when Earlier is set.
    private readonly Guid _messageA = Guid.NewGuid();
    private readonly Guid _messageB = Guid.NewGuid();
    private readonly Guid _earlier = Guid.NewGuid();
    private readonly Guid _fileZ = Guid.NewGuid();

    // Newest first, as the server sends them.
    private ConversationView Thread(Guid id) =>
      new(
        Summary(id),
        [
          id == A
            ? Message(_messageA, FileA, "a.pdf", Now)
            : Message(_messageB, FileB, "b.pdf", Now),
          .. Earlier && id == A
            ? [Message(_earlier, _fileZ, "z.pdf", Now.AddHours(-1))]
            : Array.Empty<MessageView>(),
        ],
        false
      );

    private static MessageView Message(
      Guid id,
      Guid file,
      string name,
      DateTime at
    ) =>
      new(
        id,
        "in",
        "file",
        "",
        "received",
        at,
        null,
        null,
        [new(file, name, "application/pdf", "stored", true, null, [])]
      );

    public async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      _reads[path] = _reads.GetValueOrDefault(path) + 1;
      if (path == "/api/messaging/templates")
        return Ok<IReadOnlyList<MessageTemplateView>>([]);
      if (path == "/api/messaging/inbox")
        return Ok(new InboxView([Summary(A), Summary(B)], false));
      foreach (var id in new[] { A, B })
      {
        if (path == $"/api/messaging/conversations/{id}")
          return Ok(Thread(id));
        if (path == $"/api/messaging/conversations/{id}/context")
          return Ok(
            new ConversationContext(
              null,
              null,
              "unmatched",
              [],
              [new(Guid.NewGuid(), 1441, "Customer", "active", [])]
            )
          );
      }
      if (path.EndsWith("/file"))
      {
        Filing = path;
        if (HoldFiling)
          await Filed.Task;
        return Ok(
          new DispatchDocumentInfo(
            Guid.NewGuid(),
            "bol",
            "a.pdf",
            "application/pdf",
            1,
            Now,
            "me"
          )
        );
      }
      if (path == "/api/fleet/drivers")
        return Ok(
          new MileageFleetList<MileageDriverOption>(
            1,
            [new(Driver, "Ann Driver", true)]
          )
        );
      if (path.EndsWith("/driver"))
      {
        Linking = path;
        if (HoldDriver)
          await Linked.Task;
        return Ok(4L);
      }
      if (path.EndsWith("/read"))
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
