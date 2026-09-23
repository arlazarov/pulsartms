using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Fleet;
using Client.Services;
using Client.Shared.Drivers.DriverContactEditor;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Fleet;

// Phone and email follow the source until changed here; the WhatsApp number
// is only ever what somebody entered for it.
[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class DriverContactEditorTests
{
  [Fact]
  public void TheWhatsAppNumberIsNotTakenFromThePhone()
  {
    using var f = new Fixture();
    var component = f.Render();
    component.WaitForElement("form");
    Assert.Equal("", component.Find("[id$='-whatsapp']").GetAttribute("value"));
    Assert.True(component.Find("[id$='-phone']").HasAttribute("disabled"));
    Assert.Contains("Source: 5558234327.", component.Markup);

    component
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Use the phone number")
      .Click();
    Assert.Equal(
      "+15558234327",
      component.Find("[id$='-whatsapp']").GetAttribute("value")
    );
    Assert.All(f.Requests, x => Assert.Equal(HttpMethod.Get, x.Method));
  }

  [Fact]
  public void AClearedPhoneIsSavedAsLocalAndEmailKeepsFollowingTheSource()
  {
    using var f = new Fixture();
    var component = f.Render();
    component.WaitForElement("form");
    component.Find("[id$='-phone-source']").Change(false);
    component.Find("[id$='-phone']").Input("");
    component.Find("form").Submit();

    component.WaitForAssertion(
      () => Assert.Contains("Contacts saved.", component.Markup)
    );
    var put = f.Requests.Single(x => x.Method == HttpMethod.Put);
    Assert.EndsWith($"/api/drivers/{f.Driver}/contact", put.Url);
    using var body = JsonDocument.Parse(put.Body!);
    var root = body.RootElement;
    Assert.False(root.GetProperty("phoneFromSource").GetBoolean());
    Assert.Equal("", root.GetProperty("phone").GetString());
    Assert.True(root.GetProperty("emailFromSource").GetBoolean());
    Assert.Equal(JsonValueKind.Null, root.GetProperty("email").ValueKind);
    Assert.Equal(3, root.GetProperty("revision").GetInt64());
  }

  [Fact]
  public void ARefusedSaveKeepsTheDraft()
  {
    using var f = new Fixture { Refuse = true };
    var component = f.Render();
    component.WaitForElement("form");
    component.Find("[id$='-whatsapp']").Input("823-4327");
    component.Find("form").Submit();

    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "country code",
          component.Find(".driver-contact__error").TextContent
        )
    );
    Assert.Equal(
      "823-4327",
      component.Find("[id$='-whatsapp']").GetAttribute("value")
    );
  }

  private sealed record Recorded(HttpMethod Method, string Url, string? Body);

  private sealed class Fixture : IDisposable
  {
    public BunitContext Context { get; } = new();
    public Guid Driver { get; } = Guid.NewGuid();
    public bool Refuse { get; init; }
    public List<Recorded> Requests { get; } = [];

    public Fixture()
    {
      var http = new HttpClient(new StubHttpMessageHandler(RespondAsync))
      {
        BaseAddress = new("https://fixture.invalid/"),
      };
      Context.Services.AddSingleton(new ApiService(http));
    }

    public IRenderedComponent<DriverContactEditor> Render() =>
      Context.Render<DriverContactEditor>(p => p.Add(x => x.DriverId, Driver));

    private DriverContactState State(bool clearedPhone = false) =>
      new(
        Driver,
        "Driver One",
        clearedPhone ? 4 : 3,
        new(
          clearedPhone ? null : "+15558234327",
          "5558234327",
          clearedPhone,
          !clearedPhone
        ),
        new("driver@example.com", "driver@example.com", false, true),
        null,
        null
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
      if (request.Method == HttpMethod.Put && Refuse)
        return new(HttpStatusCode.BadRequest)
        {
          Content = JsonContent.Create(
            new RequestResponseDTO<DriverContactState>
            {
              Success = false,
              Errors =
              [
                "Enter the WhatsApp number with + and country code, "
                  + "or as a 10-digit US or Canada number.",
              ],
            }
          ),
        };
      return new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<DriverContactState>
          {
            Success = true,
            Response = State(request.Method == HttpMethod.Put),
          }
        ),
      };
    }

    public void Dispose() => Context.Dispose();
  }
}
