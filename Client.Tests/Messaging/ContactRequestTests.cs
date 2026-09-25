using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Tests.Support;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// Outside the reply window, Request contact sends PulsR's contact request
// template on an explicit click, once per retry key. Until an
// administrator records it as approved for the number it says it is
// waiting for approval, and nothing can be sent with it.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class ContactRequestTests
{
  private static readonly Guid A = Guid.NewGuid();
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
  public async Task UntilItIsRecordedItWaitsForApproval()
  {
    var api = new Api(recorded: false);
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));

    page.WaitForAssertion(
      () => Assert.Contains("Waiting for approval", page.Markup)
    );
    Assert.True(Request(page).HasAttribute("disabled"));
    Assert.Empty(api.Requests);
  }

  [Fact]
  public async Task AClickSendsItOnceAndATryAgainKeepsItsKey()
  {
    var api = new Api(recorded: true) { FailFirst = true };
    await using var context = Context(api);
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, A));
    page.WaitForAssertion(
      () => Assert.False(Request(page).HasAttribute("disabled"))
    );
    page.Settle();

    await Request(page).ClickAsync(new());
    page.WaitForAssertion(() => Assert.Single(api.Requests));
    page.Settle();
    await Request(page).ClickAsync(new());
    page.WaitForAssertion(() => Assert.Equal(2, api.Requests.Count));

    Assert.Equal(
      api.Requests[0].IdempotencyKey,
      api.Requests[1].IdempotencyKey
    );
    Assert.All(
      api.Requests,
      x => Assert.Equal(("contact_request", 0), (x.Name, x.Parameters.Count))
    );
    page.Settle();
    await Request(page).ClickAsync(new());
    page.WaitForAssertion(() => Assert.Equal(3, api.Requests.Count));
    Assert.NotEqual(
      api.Requests[1].IdempotencyKey,
      api.Requests[2].IdempotencyKey
    );
  }

  private static AngleSharp.Dom.IElement Request(
    IRenderedComponent<MessagesPage> page
  ) =>
    page.FindAll("button")
      .Single(x => x.TextContent.Trim() == "Request contact");

  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.JSInterop.SetupModule("./js/generated/shared/messagingChannel.js");
    context.JSInterop.SetupModule("./js/generated/shared/messagingNotices.js");
    return context;
  }

  private sealed class Api(bool recorded)
  {
    public bool FailFirst { get; init; }
    public List<TemplateRequest> Requests { get; } = [];

    public async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      if (path == "/api/messaging/templates")
        return Ok<IReadOnlyList<MessageTemplateView>>(
          recorded
            ?
            [
              new(
                "contact_request",
                "en_US",
                1,
                "Dispatch at {{company_name}} would like to speak with you. "
                  + "Please reply when it’s safe.",
                "contactRequest"
              ),
            ]
            : []
        );
      if (path == "/api/messaging/inbox")
        return Ok(new InboxView([], false));
      if (path == $"/api/messaging/conversations/{A}/templates")
      {
        Requests.Add(
          (await request.Content!.ReadFromJsonAsync<TemplateRequest>(ct))!
        );
        if (FailFirst && Requests.Count == 1)
          return new(HttpStatusCode.BadGateway);
        return Ok(
          new MessageView(
            Guid.NewGuid(),
            "out",
            "template",
            "Dispatch would like to speak with you.",
            "queued",
            Now,
            null,
            null,
            []
          )
        );
      }
      if (path == $"/api/messaging/conversations/{A}")
        return Ok(
          new ConversationView(
            new(
              A,
              "+15550000000",
              null,
              "Ann A",
              "",
              Now,
              null,
              false,
              0,
              null,
              null,
              1
            ),
            [],
            false
          )
        );
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
