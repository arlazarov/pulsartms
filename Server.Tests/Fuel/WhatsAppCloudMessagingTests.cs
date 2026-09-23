using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Models.Messaging;
using Infrastructure.Integrations.WhatsApp;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Fuel;

// One request per send against the Cloud API, never repeated. An answer
// that did not come is unknown, and a provider's error text never leaves
// the adapter - only its number.
[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class WhatsAppCloudMessagingTests
{
  [Fact]
  public async Task ATextGoesOnceToTheCarriersNumberAndItsIdIsKept()
  {
    var http = new Handler(
      HttpStatusCode.OK,
      """{"messaging_product":"whatsapp","contacts":[{"input":"+15558234327","wa_id":"15558234327"}],"messages":[{"id":"wamid.ABC"}]}"""
    );
    var result = await Messaging(http)
      .SendTextAsync(
        "+15558234327",
        "Fuel for this shift:\n1. Fill up",
        default
      );

    Assert.Equal(DriverMessageOutcome.Accepted, result.Outcome);
    Assert.Equal("wamid.ABC", result.ProviderMessageId);
    var request = Assert.Single(http.Requests);
    Assert.Equal(
      "https://graph.facebook.com/v26.0/123456/messages",
      request.Url
    );
    Assert.Equal("Bearer token-1", request.Authorization);
    using var body = JsonDocument.Parse(request.Body);
    var root = body.RootElement;
    Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
    Assert.Equal("+15558234327", root.GetProperty("to").GetString());
    Assert.Equal("text", root.GetProperty("type").GetString());
    Assert.False(
      root.GetProperty("text").GetProperty("preview_url").GetBoolean()
    );
  }

  [Fact]
  public async Task ARefusalKeepsOnlyTheNumberOfItsError()
  {
    var result = await Messaging(
        new Handler(
          HttpStatusCode.BadRequest,
          """{"error":{"message":"Recipient +15558234327 is not a valid WhatsApp user","code":131026}}"""
        )
      )
      .SendTextAsync("+15558234327", "x", default);

    Assert.Equal(
      new DriverMessageSendResult(
        DriverMessageOutcome.Rejected,
        ErrorCode: 131026
      ),
      result
    );
  }

  [Theory]
  [InlineData("server")]
  [InlineData("timeout")]
  [InlineData("dropped")]
  [InlineData("no-id")]
  public async Task AnAnswerThatDidNotComeIsUnknownAndNotRepeated(string how)
  {
    var http = how switch
    {
      "server" => new Handler(HttpStatusCode.BadGateway, "{}"),
      "timeout" => new Handler(new TaskCanceledException()),
      "dropped" => new Handler(new HttpRequestException("reset")),
      _ => new Handler(HttpStatusCode.OK, """{"messages":[]}"""),
    };

    var result = await Messaging(http)
      .SendTextAsync("+15558234327", "x", default);

    Assert.Equal(DriverMessageOutcome.Unknown, result.Outcome);
    Assert.Single(http.Requests);
  }

  [Theory]
  [InlineData("")]
  [InlineData("12/../34")]
  public async Task NothingGoesWithoutCompleteSafeCredentials(string id)
  {
    var http = new Handler(HttpStatusCode.OK, "{}");
    var messaging = new WhatsAppCloudMessaging(
      new HttpClient(http),
      new StubProviderCredentials(
        ("phoneNumberId", id),
        ("accessToken", "token-1"),
        ("appSecret", "secret"),
        ("verifyToken", "verify")
      ),
      new ConfigurationBuilder().Build()
    );

    Assert.False(await messaging.IsConfiguredAsync(default));
    Assert.Equal(
      DriverMessageOutcome.NotConfigured,
      (await messaging.SendTextAsync("+15558234327", "x", default)).Outcome
    );
    Assert.Empty(http.Requests);
  }

  [Fact]
  public void OnlyTheExactSignatureOfTheBodyIsAccepted()
  {
    var body = Encoding.UTF8.GetBytes(
      """{"object":"whatsapp_business_account"}"""
    );
    var signature =
      "sha256="
      + Convert
        .ToHexString(
          HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), body)
        )
        .ToLowerInvariant();

    Assert.True(WhatsAppCloudMessaging.Signed(body, signature, "secret"));
    Assert.False(WhatsAppCloudMessaging.Signed(body, signature, "other"));
    Assert.False(
      WhatsAppCloudMessaging.Signed([.. body, (byte)' '], signature, "secret")
    );
    Assert.False(WhatsAppCloudMessaging.Signed(body, null, "secret"));
    Assert.False(WhatsAppCloudMessaging.Signed(body, signature[7..], "secret"));
  }

  private static WhatsAppCloudMessaging Messaging(Handler http) =>
    new(
      new HttpClient(http),
      new StubProviderCredentials(
        ("phoneNumberId", "123456"),
        ("accessToken", "token-1"),
        ("appSecret", "secret"),
        ("verifyToken", "verify")
      ),
      new ConfigurationBuilder().Build()
    );

  private sealed record Sent(string Url, string? Authorization, string Body);

  private sealed class Handler : HttpMessageHandler
  {
    private readonly HttpStatusCode status;
    private readonly string body = "";
    private readonly Exception? failure;

    public Handler(HttpStatusCode status, string body) =>
      (this.status, this.body) = (status, body);

    public Handler(Exception failure) => this.failure = failure;

    public List<Sent> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      Requests.Add(
        new(
          request.RequestUri!.ToString(),
          request.Headers.Authorization?.ToString(),
          await request.Content!.ReadAsStringAsync(ct)
        )
      );
      if (failure is not null)
        throw failure;
      return new(status)
      {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
      };
    }
  }
}
