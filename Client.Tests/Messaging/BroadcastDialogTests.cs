using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Pages.Messages;
using Client.Tests.Support;

namespace Client.Tests.Messaging;

// The dialog previews who can be sent the message and why not, sends to
// the drivers still ticked, keeps one retry key until the broadcast is
// made, and shows each driver's own outcome, withdrawn after a cancel.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class BroadcastDialogTests
{
  private static readonly Guid Ann = Guid.NewGuid();
  private static readonly Guid Bob = Guid.NewGuid();
  private static readonly Guid Cid = Guid.NewGuid();

  [Fact]
  public void PreviewShowsCountsReasonsAndSendsOnlyTheTicked()
  {
    var api = new Api { FailFirstSend = true };
    using var context = Context(api);
    var dialog = context.Render<BroadcastDialog>(x => x.Add(p => p.Open, true));

    dialog.Find("textarea").Input("Road closed at exit 12");
    dialog
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Preview")
      .Click();
    dialog.WaitForAssertion(
      () => Assert.Contains("2 of 3 can be sent it.", Text(dialog))
    );
    Assert.Contains("Has not written in the last 24 hours", dialog.Markup);
    var boxes = dialog.FindAll("input[type=checkbox]");
    Assert.True(boxes[2].HasAttribute("disabled"));

    dialog.FindAll("input[type=checkbox]")[1].Change(false);
    Send(dialog, "Send to 1");
    dialog.WaitForAssertion(() => Assert.Contains("Try again", dialog.Markup));
    Send(dialog, "Send to 1");
    dialog.WaitForAssertion(
      () => Assert.Contains("Waiting to send", dialog.Markup)
    );

    Assert.Equal(2, api.Sends.Count);
    Assert.Equal(api.Sends[0].IdempotencyKey, api.Sends[1].IdempotencyKey);
    var request = api.Sends[1].Request;
    Assert.Equal("selected", request.Scope);
    Assert.Equal([Ann], request.DriverIds);
    Assert.Equal("Road closed at exit 12", request.Text);

    dialog
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Cancel what has not gone")
      .Click();
    dialog.WaitForAssertion(() => Assert.Contains("Not sent", dialog.Markup));
  }

  [Fact]
  public void EveryoneTickedSendsTheScopeNotAList()
  {
    var api = new Api();
    using var context = Context(api);
    var dialog = context.Render<BroadcastDialog>(x => x.Add(p => p.Open, true));
    dialog.Find("textarea").Input("Hello");
    dialog
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Preview")
      .Click();
    dialog.WaitForAssertion(() => Assert.Contains("Send to 2", dialog.Markup));
    Send(dialog, "Send to 2");
    dialog.WaitForAssertion(() => Assert.Single(api.Sends));
    Assert.Equal("all", api.Sends[0].Request.Scope);
    Assert.Null(api.Sends[0].Request.DriverIds);
  }

  // Markup wraps its text; a browser shows each run of spaces as one.
  private static string Text(IRenderedComponent<BroadcastDialog> dialog) =>
    Regex.Replace(dialog.Markup, @"\s+", " ");

  private static void Send(
    IRenderedComponent<BroadcastDialog> dialog,
    string label
  ) =>
    dialog.FindAll("button").Single(x => x.TextContent.Trim() == label).Click();

  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    return context;
  }

  private sealed class Api
  {
    private static readonly JsonSerializerOptions Json = new(
      JsonSerializerDefaults.Web
    );

    public bool FailFirstSend { get; init; }
    public List<BroadcastBody> Sends { get; } = [];

    private static BroadcastView View(string status) =>
      new(
        Guid.NewGuid(),
        "text",
        "Road closed",
        "Selected drivers",
        DateTime.UtcNow,
        status == "withdrawn" ? DateTime.UtcNow : null,
        [new(Ann, "Ann A", Guid.NewGuid(), Guid.NewGuid(), status, null)]
      );

    public async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      if (path == "/api/messaging/broadcasts/preview")
        return Ok(
          new BroadcastPreview(
            [
              new(Ann, "Ann A", "+15550000001", true, null),
              new(Bob, "Bob B", "+15550000002", true, null),
              new(
                Cid,
                "Cid C",
                "+15550000003",
                false,
                "Has not written in the last 24 hours: only a template can go."
              ),
            ],
            2,
            "Road closed"
          )
        );
      if (path == "/api/messaging/broadcasts")
      {
        Sends.Add(
          (await request.Content!.ReadFromJsonAsync<BroadcastBody>(Json, ct))!
        );
        if (FailFirstSend && Sends.Count == 1)
          return new(HttpStatusCode.ServiceUnavailable)
          {
            Content = JsonContent.Create(
              new RequestResponseDTO<BroadcastView>
              {
                Success = false,
                Errors = ["Try again"],
              }
            ),
          };
        return Ok(View("queued"));
      }
      if (path.EndsWith("/cancel"))
        return Ok(View("withdrawn"));
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
