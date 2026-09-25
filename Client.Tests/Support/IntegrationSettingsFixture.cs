using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Integrations;
using Client.Pages.Settings;

namespace Client.Tests.Support;

internal static class IntegrationSettingsFixture
{
  // The page reads its providers and then the WhatsApp webhook address, and
  // shows the provider cards only once both are answered; a configured
  // WhatsApp card then reads its approved templates on its own. Whether a
  // read completes before the page first yields decides how many times the
  // cards are drawn, and a card drawn again replaces its buttons' handlers,
  // so a button found in between can be gone when it is clicked. Each read
  // is held and answered in turn, and the test waits for what that answer
  // shows: the cards for the webhook address (they cannot appear before
  // it), the templates section leaving its loading state for the templates.
  // After that the page has nothing more on its way.
  public static IRenderedComponent<IntegrationSettings> RenderLoaded(
    ClientComponentContext context
  )
  {
    var providers = context.Hold("/api/settings/integrations");
    var webhook = context.Hold("/api/settings/integrations/whatsapp/webhook");
    var templates = context.Hold(
      "/api/settings/integrations/whatsapp/templates"
    );
    var page = context.Render<IntegrationSettings>();
    providers.Release();
    webhook.Release(
      page,
      () => Assert.NotEmpty(page.FindAll("[data-provider] button"))
    );
    if (page.FindAll(".whatsapp-templates").Count > 0)
      templates.Release(
        page,
        () => Assert.Empty(page.FindAll(".whatsapp-templates [role=status]"))
      );
    return page;
  }

  public static IReadOnlyList<IntegrationConnectionState> States(
    long revision = 3,
    bool saved = false
  ) =>
    [
      State("torqueai", revision, saved),
      State("samsara", revision, saved),
      State("google-email", revision, saved),
      State("whatsapp", revision, saved),
    ];

  public static IntegrationConnectionState State(
    string provider,
    long revision = 3,
    bool saved = false
  ) => new(provider, true, saved, saved, revision, null, (
        provider switch
        {
          "google-email" => new[]
          {
            "clientId",
            "clientSecret",
            "refreshToken",
          },
          "whatsapp" =>
          [
            "phoneNumberId",
            "accessToken",
            "appSecret",
            "verifyToken",
          ],
          _ => ["apiKey"],
        }
      ).Select(name => new IntegrationCredentialFieldState(name, true)).ToArray());

  public static HttpResponseMessage Response<T>(T value) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new RequestResponseDTO<T> { Success = true, Response = value }
      ),
    };

  public static HttpResponseMessage ListResponse(
    long revision = 3,
    bool saved = false
  ) => Response(States(revision, saved));
}
