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

namespace Client.Tests.Routing;

// Beside a conversation, the driver and their truck; on a driver's file,
// filing to a load. Nothing is filed until the dispatcher presses File;
// the driver's loads are offered only when they are on one truck, and any
// other load is filed by its number.
[Trait("Category", "Routing")]
[Trait("Kind", "Component")]
public sealed class MessageFilingPageTests
{
  private static readonly Guid Ann = Guid.NewGuid();
  private static readonly Guid Load = Guid.NewGuid();
  private static readonly Guid File = Guid.NewGuid();

  [Fact]
  public async Task OnlyFilePressedFilesAndTheSuggestionIsTheDriversLoad()
  {
    var api = new Api(OneTruck());
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));
    page.WaitForAssertion(() => Assert.Contains("Truck 11006", page.Markup));
    Assert.Contains("Toronto → Ottawa", page.Markup);

    await Button(page, "File to load").ClickAsync(new());
    Assert.Empty(api.Filed);
    page.WaitForElement("[id^=filing-kind]").Change("pod");
    await page.Find(".messages__filing").SubmitAsync();

    var filed = Assert.Single(api.Filed);
    Assert.Equal(new FileRequest(Load, null, "pod"), filed);
    page.WaitForAssertion(() => Assert.Contains("Filed to", page.Markup));
    Assert.Equal(2, api.ThreadReads);
  }

  [Fact]
  public async Task OnSeveralTrucksNoLoadIsOfferedAndANumberIsFiled()
  {
    var api = new Api(
      new(
        Guid.NewGuid(),
        "Ann Driver",
        "several-trucks",
        [
          new(Guid.NewGuid(), "11006", "driver"),
          new(Guid.NewGuid(), "11007", "co-driver"),
        ],
        []
      )
    );
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));
    page.WaitForAssertion(
      () => Assert.Contains("11006 (driver), 11007 (co-driver)", page.Markup)
    );

    await Button(page, "File to load").ClickAsync(new());
    Assert.Single(page.FindAll("[id^=filing-load] option"));
    page.WaitForElement("[id^=filing-number]").Change("1407");
    await page.Find(".messages__filing").SubmitAsync();

    Assert.Equal(new FileRequest(null, 1407, "bol"), Assert.Single(api.Filed));
  }

  [Fact]
  public async Task ADispatcherChoosesTheDriverAtTheRevisionShown()
  {
    var api = new Api(new(null, null, "unmatched", [], []));
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));
    page.WaitForAssertion(
      () => Assert.Contains("No driver matches this number.", page.Markup)
    );

    await Button(page, "Choose driver").ClickAsync(new());
    page.WaitForElement("[id^=driver-] option[value]");
    page.Find("[id^=driver-]").Change(api.Driver.ToString());
    await page.Find(".messages__context .messages__filing").SubmitAsync();

    Assert.Equal(new DriverRequest(api.Driver, 3), Assert.Single(api.Linked));
    page.WaitForAssertion(() => Assert.Equal(2, api.ContextReads));
  }

  private static ConversationContext OneTruck() =>
    new(
      Guid.NewGuid(),
      "Ann Driver",
      "one-truck",
      [new(Guid.NewGuid(), "11006", "assigned")],
      [new(Load, 1441, "Fixture Customer", "active", ["Toronto", "Ottawa"])]
    );

  private static IElement Button(
    IRenderedComponent<MessagesPage> page,
    string text
  ) => page.FindAll("button").First(x => x.TextContent.Trim() == text);

  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.Services.AddSingleton<TokenStorageService>();
    context.Services.AddSingleton<MessagingSignals>();
    return context;
  }

  private sealed class Api(ConversationContext context)
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
    private bool _filed;

    public Guid Driver { get; } = Guid.NewGuid();
    public List<FileRequest> Filed { get; } = [];
    public List<DriverRequest> Linked { get; } = [];
    public int ThreadReads { get; private set; }
    public int ContextReads { get; private set; }

    private ConversationSummary Summary() =>
      new(
        Ann,
        "+15558234327",
        null,
        null,
        "Bill",
        Now,
        Now,
        true,
        0,
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
        return Ok(new InboxView([Summary()], false));
      if (path == $"/api/messaging/conversations/{Ann}/context")
      {
        ContextReads++;
        return Ok(context);
      }
      if (path == $"/api/messaging/conversations/{Ann}")
      {
        ThreadReads++;
        return Ok(
          new ConversationView(
            Summary(),
            [
              new(
                Guid.NewGuid(),
                "in",
                "file",
                "",
                "received",
                Now,
                null,
                null,
                [
                  new(
                    File,
                    "bol.pdf",
                    "application/pdf",
                    "stored",
                    true,
                    null,
                    _filed ? [new(Load, 1441, "pod")] : []
                  ),
                ]
              ),
            ],
            false
          )
        );
      }
      if (path == $"/api/messaging/attachments/{File}/file")
      {
        var filed = await request.Content!.ReadFromJsonAsync<FileRequest>(ct);
        Filed.Add(filed!);
        _filed = true;
        return Ok(
          new DispatchDocumentInfo(
            Guid.NewGuid(),
            "pod",
            "bol.pdf",
            "application/pdf",
            12,
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
      if (path == $"/api/messaging/conversations/{Ann}/driver")
      {
        Linked.Add(
          (await request.Content!.ReadFromJsonAsync<DriverRequest>(ct))!
        );
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
