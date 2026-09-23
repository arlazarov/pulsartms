using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Shared.Fuel.FuelSendPlan;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Fleet;

// The Send plan window: what goes to the driver, and the one place a
// dispatcher says it went. Opening and copying record nothing.
[Trait("Category", "Fuel")]
[Trait("Kind", "Component")]
public sealed class FuelSendPlanTests
{
  [Fact]
  public void ItShowsThisShiftAndSaysSendingIsByHand()
  {
    using var f = new Fixture();
    var component = f.Render();
    component.WaitForAssertion(() =>
      Assert.Single(component.FindAll(".fuel-send-plan__lines li"))
    );
    Assert.Contains("Driver on duty", component.Find(".fuel-send-plan__state").TextContent);
    Assert.Contains("Copying does not mark it sent", component.Markup);
    Assert.Contains(
      "WhatsApp is not set up",
      component.Find(".fuel-send-plan__recipient").TextContent
    );
    Assert.True(
      component
        .FindAll("button")
        .Single(x => x.TextContent.Trim() == "Send via WhatsApp")
        .HasAttribute("disabled")
    );
    Assert.Equal(
      "Fuel for this shift:\n1. Ahead: fuel at LOVES",
      component.Find("textarea").GetAttribute("value")
    );
  }

  [Fact]
  public void CopyingRecordsNothing()
  {
    using var f = new Fixture();
    f.Context.JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true)
      .SetVoidResult();
    var component = f.Render();
    component.WaitForElement(".fuel-send-plan__lines li");
    component.FindAll("button").Single(x => x.TextContent == "Copy message").Click();
    component.WaitForAssertion(() =>
      Assert.Contains("not marked as sent", component.Find(".fuel-send-plan__status").TextContent)
    );
    Assert.All(f.Requests, x => Assert.Equal(HttpMethod.Get, x.Method));
    Assert.Equal(0, f.Sent);
  }

  [Fact]
  public void MarkingSentConfirmsTheVersionThatWasShown()
  {
    using var f = new Fixture();
    var component = f.Render();
    component.WaitForElement(".fuel-send-plan__lines li");
    component.FindAll("button").Single(x => x.TextContent.Trim() == "Mark as sent").Click();

    component.WaitForAssertion(() =>
      Assert.Equal("Sent", component.Find(".fleet-fuel-visit__sent").TextContent)
    );
    var post = f.Requests.Single(x => x.Method == HttpMethod.Post);
    Assert.EndsWith($"/{f.Dispatch}/planning/fuel/issue/sent", post.Url);
    using var body = JsonDocument.Parse(post.Body!);
    Assert.Equal(
      f.CalculatedAt,
      body.RootElement.GetProperty("expectedCalculatedAt").GetDateTime()
    );
    Assert.Equal(
      "visit-1",
      body.RootElement.GetProperty("visitKeys")[0].GetString()
    );
    Assert.Equal(7, body.RootElement.GetProperty("assignmentRevision").GetInt64());
    Assert.Equal(1, f.Sent);
    Assert.True(
      component
        .FindAll("button")
        .Single(x => x.TextContent.Trim() == "Mark as sent")
        .HasAttribute("disabled")
    );
  }

  [Fact]
  public void APlanThatMovedIsNotMarkedSent()
  {
    using var f = new Fixture { Conflict = true };
    var component = f.Render();
    component.WaitForElement(".fuel-send-plan__lines li");
    component.FindAll("button").Single(x => x.TextContent.Trim() == "Mark as sent").Click();
    component.WaitForAssertion(() =>
      Assert.Contains("changed since it was opened", component.Find(".fuel-send-plan__error").TextContent)
    );
    Assert.Empty(component.FindAll(".fleet-fuel-visit__sent"));
    Assert.Equal(0, f.Sent);
  }

  [Theory]
  [InlineData("awaitingDuty", "Awaiting duty")]
  [InlineData("hosUnknown", "Driver hours unknown")]
  public void RestAndUnknownHoursAreSaidPlainly(string state, string text)
  {
    using var f = new Fixture();
    f.Preview = f.Preview with { IssueState = state, Critical = true };
    var component = f.Render();
    component.WaitForAssertion(() =>
      Assert.StartsWith(text, component.Find(".fuel-send-plan__state").TextContent)
    );
    Assert.Contains("out of reach", component.Find(".fuel-send-plan__critical").TextContent);
  }

  [Fact]
  public void WhatsAppSendsTheShownVersionAndSaysAcceptedIsNotDelivered()
  {
    using var f = new Fixture();
    f.Ready();
    var component = f.Render();
    component.WaitForElement(".fuel-send-plan__lines li");
    Assert.Contains(
      "Sends to Driver One at +15558234327.",
      component.Find(".fuel-send-plan__recipient").TextContent
    );
    component
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Send via WhatsApp")
      .Click();

    component.WaitForAssertion(() =>
      Assert.Equal(
        "WhatsApp accepted the message. Not delivered yet.",
        component.Find(".fuel-send-plan__delivery").TextContent
      )
    );
    var post = f.Requests.Single(x => x.Method == HttpMethod.Post);
    Assert.EndsWith($"/{f.Dispatch}/planning/fuel/issue/whatsapp", post.Url);
    using var body = JsonDocument.Parse(post.Body!);
    var plan = body.RootElement.GetProperty("plan");
    Assert.Equal(f.CalculatedAt, plan.GetProperty("expectedCalculatedAt").GetDateTime());
    Assert.Equal(7, plan.GetProperty("assignmentRevision").GetInt64());
    Assert.False(body.RootElement.GetProperty("sendAgain").GetBoolean());
    Assert.Equal(1, f.Sent);
  }

  [Fact]
  public void AnUnansweredAttemptIsSentAgainOnlyByItsOwnButton()
  {
    using var f = new Fixture();
    f.Ready();
    f.Preview = f.Preview with
    {
      LastMessage = new("unknown", f.CalculatedAt, null, true),
    };
    var component = f.Render();
    component.WaitForElement(".fuel-send-plan__delivery--warn");
    Assert.Contains(
      "may have been delivered",
      component.Find(".fuel-send-plan__delivery").TextContent
    );
    Assert.DoesNotContain(
      component.FindAll("button"),
      x => x.TextContent.Trim() == "Send via WhatsApp"
    );
    component
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Send again via WhatsApp")
      .Click();
    component.WaitForAssertion(() =>
      Assert.Single(f.Requests, x => x.Method == HttpMethod.Post)
    );
    using var body = JsonDocument.Parse(
      f.Requests.Single(x => x.Method == HttpMethod.Post).Body!
    );
    Assert.True(body.RootElement.GetProperty("sendAgain").GetBoolean());
  }

  [Fact]
  public void AClosedWindowSaysWhyAndSendsNothing()
  {
    using var f = new Fixture();
    f.Ready("outsideWindow");
    var component = f.Render();
    component.WaitForElement(".fuel-send-plan__lines li");
    Assert.Contains(
      "last 24 hours",
      component.Find(".fuel-send-plan__recipient").TextContent
    );
    Assert.True(
      component
        .FindAll("button")
        .Single(x => x.TextContent.Trim() == "Send via WhatsApp")
        .HasAttribute("disabled")
    );
  }

  private sealed record Recorded(HttpMethod Method, string Url, string? Body);

  private sealed class Fixture : IDisposable
  {
    public BunitContext Context { get; } = new();
    public Guid Dispatch { get; } = Guid.NewGuid();
    public DateTime CalculatedAt { get; } =
      new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    public bool Conflict { get; init; }
    public int Sent { get; private set; }
    public List<Recorded> Requests { get; } = [];
    public FuelIssuePreview Preview { get; set; }

    public Fixture()
    {
      Preview = new(
        Guid.NewGuid(),
        CalculatedAt,
        null,
        7,
        "ready",
        null,
        false,
        [new("visit-1", "Ahead: fuel at LOVES", false, false)],
        "Fuel for this shift:\n1. Ahead: fuel at LOVES"
      );
      var http = new HttpClient(new StubHttpMessageHandler(RespondAsync))
      {
        BaseAddress = new("https://fixture.invalid/"),
      };
      Context.Services.AddSingleton(new ApiService(http));
    }

    public void Ready(string state = "ready") =>
      Preview = Preview with
      {
        Recipient = new(
          Guid.NewGuid(),
          "Driver One",
          "+15558234327",
          state,
          null
        ),
      };

    public IRenderedComponent<FuelSendPlan> Render() =>
      Context.Render<FuelSendPlan>(p =>
        p.Add(x => x.DispatchId, Dispatch)
          .Add(x => x.TruckNumber, "54777")
          .Add(x => x.Sent, () => Sent++)
      );

    private async Task<HttpResponseMessage> RespondAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var body = request.Content is null
        ? null
        : await request.Content.ReadAsStringAsync(ct);
      Requests.Add(new(request.Method, request.RequestUri!.AbsolutePath, body));
      if (request.Method == HttpMethod.Post && Conflict)
        return new(HttpStatusCode.Conflict)
        {
          Content = JsonContent.Create(
            new RequestResponseDTO<FuelIssuePreview>
            {
              Success = false,
              Errors =
              [
                "The fuel plan changed since it was opened. Open it again, send the new plan, then confirm.",
              ],
            }
          ),
        };
      var whatsApp = request.RequestUri!.AbsolutePath.EndsWith("/whatsapp");
      var preview =
        request.Method != HttpMethod.Post ? Preview
        : whatsApp ? Preview with
        {
          Lines = [Preview.Lines[0] with { Sent = true, Delivery = "accepted" }],
          LastMessage = new("accepted", CalculatedAt, null, false),
        }
        : Preview with
        {
          Lines = [Preview.Lines[0] with { Sent = true }],
        };
      return new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<FuelIssuePreview>
          {
            Success = true,
            Response = preview,
          }
        ),
      };
    }

    public void Dispose() => Context.Dispose();
  }
}
