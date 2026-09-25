using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Pages.Settings;
using Client.Tests.Support;

namespace Client.Tests.Messaging;

// An administrator records the templates Meta approved for the current
// number and removes them after confirming. A refused entry keeps its draft.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class WhatsAppTemplatesTests
{
  private static readonly ApprovedTemplateView Ready = new(
    Guid.NewGuid(),
    "fuel_plan_ready",
    "en_US",
    1,
    "Your fuel plan for load {{1}} is ready."
  );

  [Fact]
  public async Task ARefusedTemplateKeepsItsDraftAndAnAcceptedOneIsListed()
  {
    var api = new Api { RefuseFirst = true };
    await using var context = new ClientComponentContext(api.SendAsync);
    var page = context.Render<WhatsAppTemplates>();
    page.WaitForAssertion(
      () => Assert.Contains("No template is recorded yet", page.Markup)
    );
    Assert.Contains("123456", page.Markup);
    Assert.Empty(page.FindAll("input"));

    await Button(page, "Record a template").ClickAsync(new());
    page.Find("#whatsapp-template-name").Input("fuel_plan_ready");
    page.Find("#whatsapp-template-parameters").Input("1");
    page.Find("#whatsapp-template-text").Input("Load {{2}}");
    await page.Find("form").SubmitAsync();
    page.WaitForAssertion(
      () =>
        Assert.Contains("must use {{1}}", page.Find("[role=alert]").TextContent)
    );
    Assert.Equal(
      "Load {{2}}",
      page.Find("#whatsapp-template-text").GetAttribute("value")
    );

    page.Find("#whatsapp-template-text").Input(Ready.Text);
    await page.Find("form").SubmitAsync();
    page.WaitForAssertion(
      () => Assert.Contains("fuel_plan_ready · en_US", page.Markup)
    );
    Assert.Equal(
      new ApprovedTemplateRequest("fuel_plan_ready", "en_US", 1, Ready.Text),
      api.Added[^1]
    );
    Assert.Empty(page.FindAll("input"));
    Assert.Contains("Template recorded.", page.Markup);
  }

  [Fact]
  public async Task ATemplateIsRemovedOnlyAfterConfirming()
  {
    var api = new Api();
    api.Templates.Add(Ready);
    await using var context = new ClientComponentContext(api.SendAsync);
    var page = context.Render<WhatsAppTemplates>();
    page.WaitForAssertion(
      () => Assert.Contains("fuel_plan_ready · en_US", page.Markup)
    );

    await Button(page, "Remove").ClickAsync(new());
    Assert.Empty(api.Removed);
    await Button(page, "Keep").ClickAsync(new());
    await Button(page, "Remove").ClickAsync(new());
    await Button(page, "Remove template").ClickAsync(new());

    page.WaitForAssertion(
      () => Assert.Contains("No template is recorded yet", page.Markup)
    );
    Assert.Equal([Ready.Id], api.Removed);
  }

  // What PulsR's own templates need from Meta, and a shortcut to record one
  // exactly as defined once Meta approved it; PulsR never says it is
  // approved on its own.
  [Fact]
  public async Task PulsrTemplatesShowWhatToSubmitAndFillTheRecordForm()
  {
    var api = new Api();
    api.Pulsr.Add(
      new(
        "contactRequest",
        "contact_request",
        "en_US",
        0,
        "Dispatch would like to speak with you. Please reply when it’s safe.",
        "{\"name\": \"contact_request\"}",
        null,
        false
      )
    );
    await using var context = new ClientComponentContext(api.SendAsync);
    var page = context.Render<WhatsAppTemplates>();
    page.WaitForAssertion(
      () => Assert.Contains("Templates PulsR uses", page.Markup)
    );
    Assert.Contains("waiting for Meta", page.Markup);
    Assert.Equal(
      "{\"name\": \"contact_request\"}",
      page.Find(".whatsapp-templates__submission pre").TextContent
    );

    await Button(page, "Record it as approved").ClickAsync(new());

    Assert.Equal(
      "contact_request",
      page.Find("#whatsapp-template-name").GetAttribute("value")
    );
    Assert.Equal(
      "Dispatch would like to speak with you. Please reply when it’s safe.",
      page.Find("#whatsapp-template-text").GetAttribute("value")
    );
    Assert.Empty(api.Added);
  }

  private static AngleSharp.Dom.IElement Button(
    IRenderedComponent<WhatsAppTemplates> page,
    string text
  ) => page.FindAll("button").Single(x => x.TextContent.Trim() == text);

  private sealed class Api
  {
    public bool RefuseFirst { get; init; }
    public List<ApprovedTemplateView> Templates { get; } = [];
    public List<ApprovedTemplateRequest> Added { get; } = [];
    public List<Guid> Removed { get; } = [];
    public List<PulsrTemplateView> Pulsr { get; } = [];

    public async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      const string templates = "/api/settings/integrations/whatsapp/templates";
      if (path == templates && request.Method == HttpMethod.Get)
        return Ok(
          new ApprovedTemplatesView("123456", [.. Templates], [.. Pulsr])
        );
      if (path == templates && request.Method == HttpMethod.Post)
      {
        var body = (
          await request.Content!.ReadFromJsonAsync<ApprovedTemplateRequest>(ct)
        )!;
        Added.Add(body);
        if (RefuseFirst && Added.Count == 1)
          return new(HttpStatusCode.BadRequest)
          {
            Content = JsonContent.Create(
              new RequestResponseDTO<ApprovedTemplateView>
              {
                Success = false,
                Errors =
                [
                  "The text must use {{1}} to {{1}}, each at least once, "
                    + "and no other placeholder.",
                ],
              }
            ),
          };
        var added = new ApprovedTemplateView(
          Ready.Id,
          body.Name,
          body.Language,
          body.Parameters,
          body.Text
        );
        Templates.Add(added);
        return Ok(added);
      }
      if (
        path.StartsWith(templates + "/")
        && request.Method == HttpMethod.Delete
      )
      {
        var id = Guid.Parse(path[(templates.Length + 1)..]);
        Removed.Add(id);
        Templates.RemoveAll(x => x.Id == id);
        return Ok(true);
      }
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
