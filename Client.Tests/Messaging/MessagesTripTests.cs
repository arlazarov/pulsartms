using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Messaging;

// Beside a conversation, as the Driver Messages prototype lays it out: the
// driver's hours with how long they have been in their status (and how
// old the hours are when that matters), the current load and its stops,
// said plainly when missing; where there is no room beside it the trip
// opens over the conversation. A dispatcher's own replies read "You", and
// their own claim is not shown to them as someone else's.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class MessagesTripTests
{
  private static readonly Guid Ann = Guid.NewGuid();
  private static readonly DateTime Now = DateTime.UtcNow;

  [Fact]
  public async Task TheDriversHoursAndLoadShowBesideTheConversation()
  {
    await using var context = Context(new Api(Hours(known: true)));

    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));

    page.WaitForAssertion(
      () => Assert.Contains("North distribution centre", page.Markup)
    );
    var trip = page.Find(".messages__context").TextContent;
    Assert.Contains("4:45", trip);
    Assert.Contains("9:20", trip);
    // How long the driver has been in the status, from the server's start
    // time; no fetch time presented as news.
    Assert.Contains("Driving for 1h 20min", trip);
    Assert.DoesNotContain("Samsara", trip);
    Assert.DoesNotContain("Hours as of", trip);
    Assert.Contains("PO 55-1180", trip);
    Assert.Contains("Toronto", trip);
    Assert.Contains("Truck 11006", page.Find(".messages__head").TextContent);
    Assert.Equal(
      "Drive 4:45 · Shift 9:20 left",
      page.Find(".messages__glance").TextContent.Trim()
    );
  }

  [Fact]
  public async Task OldClocksAreSaidAndNoDurationIsClaimed()
  {
    await using var context = Context(
      new Api(Hours(known: true, updated: Now.AddMinutes(-12)))
    );

    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));

    page.WaitForAssertion(() => Assert.Contains("12 min ago", page.Markup));
    var trip = page.Find(".messages__context").TextContent;
    Assert.Contains("Hours as of", trip);
    Assert.Contains("Driving", trip);
    Assert.DoesNotContain("Driving for", trip);
  }

  [Fact]
  public async Task AnUnknownStatusStartShowsTheStatusAlone()
  {
    await using var context = Context(
      new Api(Hours(known: true)) { Duty = new("driving", null) }
    );

    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));

    page.WaitForAssertion(
      () => Assert.Contains("North distribution centre", page.Markup)
    );
    var trip = page.Find(".messages__context").TextContent;
    Assert.Contains("Driving", trip);
    Assert.DoesNotContain("Driving for", trip);
    Assert.DoesNotContain("Hours as of", trip);
  }

  [Fact]
  public async Task MissingHoursAreSaidPlainlyNotShownAsZero()
  {
    await using var context = Context(new Api(Hours(known: false)));

    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));

    page.WaitForAssertion(
      () =>
        Assert.Contains(
          "Samsara has no hours for this driver now.",
          page.Markup
        )
    );
    Assert.Empty(page.FindAll(".messages__context .driver-hours"));
    Assert.Empty(page.FindAll(".messages__glance"));
  }

  [Fact]
  public async Task TripOpensOverTheConversationAndCloses()
  {
    await using var context = Context(new Api(Hours(known: true)));
    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));
    var trip = page.WaitForElement(".messages__trip");
    Assert.Equal("false", trip.GetAttribute("aria-expanded"));

    await trip.ClickAsync(new());

    Assert.Contains("shows-trip", page.Find(".messages-page").ClassName);
    Assert.Equal(
      "true",
      page.Find(".messages__trip").GetAttribute("aria-expanded")
    );
    await page.Find(".messages__close").ClickAsync(new());
    Assert.DoesNotContain("shows-trip", page.Find(".messages-page").ClassName);
  }

  [Fact]
  public async Task OwnRepliesReadYouAndOwnClaimIsNotAColleagues()
  {
    await using var context = Context(
      new Api(Hours(known: true)) { ClaimedBy = "Me Dispatcher" }
    );
    context.Authorization.SetAuthorized("Me Dispatcher");

    var page = context.Render<MessagesPage>(x => x.Add(p => p.Id, Ann));

    page.WaitForAssertion(
      () =>
        Assert.Equal("You", page.Find(".messages__author").TextContent.Trim())
    );
    Assert.Empty(page.FindAll(".messages__banner"));
    Assert.Empty(page.FindAll(".messages__tag.is-claim"));
    // All pressed; Unread and Archive not.
    Assert.Equal(
      ["true", "false", "false"],
      page.FindAll(".messages__chip")
        .Select(x => x.GetAttribute("aria-pressed"))
    );
  }

  private static ContextHours Hours(bool known, DateTime? updated = null) =>
    known
      ? new(
        true,
        6 * 3600_000,
        4 * 3600_000 + 45 * 60_000,
        9 * 3600_000 + 20 * 60_000,
        40 * 3600_000,
        updated ?? Now.AddMinutes(-2),
        "driving"
      )
      : new(false, null, null, null, null, null, null);

  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.Services.AddSingleton<TokenStorageService>();
    context.Services.AddSingleton<MessagingSignals>();
    return context;
  }

  private sealed class Api(ContextHours hours)
  {
    public string? ClaimedBy { get; init; }

    public ContextDuty Duty { get; init; } =
      new("driving", DateTimeOffset.UtcNow.AddMinutes(-80));

    private ConversationSummary Summary() =>
      new(
        Ann,
        "+15558234327",
        Guid.NewGuid(),
        "Ann Driver",
        "On my way",
        Now,
        Now,
        true,
        0,
        ClaimedBy,
        ClaimedBy is null ? null : Now.AddMinutes(1),
        3
      );

    public Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      return Task.FromResult(
        path switch
        {
          "/api/messaging/templates" => Ok<IReadOnlyList<MessageTemplateView>>(
            []
          ),
          "/api/messaging/inbox" => Ok(new InboxView([Summary()], false)),
          _ when path == $"/api/messaging/conversations/{Ann}/context" => Ok(
            new ConversationContext(
              Guid.NewGuid(),
              "Ann Driver",
              "one-truck",
              [new(Guid.NewGuid(), "11006", "driver")],
              [
                new(
                  Guid.NewGuid(),
                  1441,
                  "Fixture Customer",
                  "active",
                  ["Windsor", "Toronto"]
                )
                {
                  OrderNumber = "PO 55-1180",
                  Stops =
                  [
                    new("North distribution centre", "Windsor"),
                    new("Lakeshore receiving", "Toronto"),
                  ],
                },
              ],
              hours,
              Duty
            )
          ),
          _ when path == $"/api/messaging/conversations/{Ann}" => Ok(
            new ConversationView(
              Summary(),
              [
                new(
                  Guid.NewGuid(),
                  "out",
                  "text",
                  "On my way",
                  "read",
                  Now,
                  "Me Dispatcher",
                  null,
                  []
                ),
              ],
              false
            )
            {
              ReadThrough = 3,
            }
          ),
          _ when path.EndsWith("/read") => Ok(true),
          _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }
      );
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
