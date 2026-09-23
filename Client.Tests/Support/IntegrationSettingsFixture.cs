using System.Net;
using System.Net.Http.Json;
using Client.Models.DTO;
using Client.Models.DTO.Integrations;

namespace Client.Tests.Support;

internal static class IntegrationSettingsFixture
{
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
