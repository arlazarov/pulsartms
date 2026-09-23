using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Application.Features.Routing.Interfaces;
using Application.Storage;
using Domain.Models.Messaging;
using Infrastructure;
using Infrastructure.Integrations.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Server.Tests.Routing;

// The local provider is for a developer's machine only: configuration
// chooses it, anything but Development refuses it at start, and it sends
// nothing anywhere. A simulated driver message must still be signed.
[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class LocalDriverMessagingTests
{
  private const string Secret = "local-secret";

  [Fact]
  public async Task OutsideDevelopmentTheLocalProviderIsRefusedAtStart()
  {
    await using var production = Services("Production");
    await using var development = Services("Development");

    var refused = Assert.Throws<OptionsValidationException>(
      () => production.GetRequiredService<IStartupValidator>().Validate()
    );
    Assert.Contains(LocalDriverMessaging.DevelopmentOnly, refused.Message);
    Assert.Throws<InvalidOperationException>(
      () => Messaging("Staging", Secret)
    );
    development.GetRequiredService<IStartupValidator>().Validate();
    await using var scope = development.CreateAsyncScope();
    Assert.IsType<LocalDriverMessaging>(
      scope.ServiceProvider.GetRequiredService<IDriverMessaging>()
    );
  }

  [Fact]
  public async Task WithoutTheSettingTheRealAdapterIsUsed()
  {
    await using var services = Services("Development", provider: null);
    await using var scope = services.CreateAsyncScope();

    Assert.IsType<WhatsAppCloudMessaging>(
      scope.ServiceProvider.GetRequiredService<IDriverMessaging>()
    );
  }

  [Fact]
  public async Task SendsAreAcceptedUnderLocalIdsWithoutLeavingTheProcess()
  {
    var messaging = Messaging("Development", Secret);
    var file = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7"));

    var text = await messaging.SendTextAsync("+15550000000", "Ok", default);
    var sent = await messaging.SendFileAsync(
      "+15550000000",
      new(file, file.Length, "application/pdf", "rate.pdf", ""),
      default
    );

    Assert.Equal(DriverMessageOutcome.Accepted, text.Outcome);
    Assert.StartsWith("local.", text.ProviderMessageId);
    Assert.NotEqual(text.ProviderMessageId, sent.ProviderMessageId);
    Assert.Equal(file.Length, file.Position);
    Assert.Equal("local", await messaging.BusinessNumberAsync(default));
  }

  [Fact]
  public async Task ASimulatedDriverMessageMustBeSignedWithTheLocalSecret()
  {
    var body = Notification("local");
    var signature =
      "sha256="
      + Convert.ToHexStringLower(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), body)
      );

    Assert.Null(
      await Messaging("Development", Secret)
        .ReadNotificationAsync(body, null, default)
    );
    Assert.Null(
      await Messaging("Development", null)
        .ReadNotificationAsync(body, signature, default)
    );
    var read = await Messaging("Development", Secret)
      .ReadNotificationAsync(body, signature, default);
    var inbound = Assert.Single(read!.Inbound);
    Assert.Equal("Loaded, rolling.", inbound.Text);
  }

  [Fact]
  public async Task MediaOpensAsASmallImageThatPassesTheCheck()
  {
    var media = await Messaging("Development", Secret)
      .OpenMediaAsync("any", default);

    await using var content = media!.Content;
    var bytes = new byte[media.Length];
    await content.ReadExactlyAsync(bytes);
    Assert.Equal(
      Convert.ToHexStringLower(SHA256.HashData(bytes)),
      media.Sha256
    );
    Assert.True(StoredFileCheck.Matches(media.MimeType, bytes));
  }

  private static LocalDriverMessaging Messaging(
    string environment,
    string? secret
  ) =>
    new(
      Options.Create(new LocalMessagingOptions { AppSecret = secret }),
      new Environment(environment)
    );

  private static ServiceProvider Services(
    string environment,
    string? provider = "local"
  )
  {
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IHostEnvironment>(new Environment(environment));
    services.AddSingleton<IIntegrationCredentials, NoCredentials>();
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?> { ["WhatsApp:Provider"] = provider }
      )
      .Build();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddInfrastructure(configuration);
    return services.BuildServiceProvider();
  }

  private static byte[] Notification(string number) =>
    JsonSerializer.SerializeToUtf8Bytes(
      new
      {
        @object = "whatsapp_business_account",
        entry = new[]
        {
          new
          {
            id = "waba",
            changes = new[]
            {
              new
              {
                field = "messages",
                value = new
                {
                  messaging_product = "whatsapp",
                  metadata = new
                  {
                    display_phone_number = "15550000000",
                    phone_number_id = number,
                  },
                  messages = new[]
                  {
                    new
                    {
                      from = "15550000001",
                      id = "wamid.local-1",
                      timestamp = "1790000000",
                      type = "text",
                      text = new { body = "Loaded, rolling." },
                    },
                  },
                },
              },
            },
          },
        },
      }
    );

  private sealed class NoCredentials : IIntegrationCredentials
  {
    public Task<IntegrationCredentialValues> GetAsync(
      string provider,
      CancellationToken ct
    ) => Task.FromResult(new IntegrationCredentialValues([]));
  }

  private sealed class Environment(string name) : IHostEnvironment
  {
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "Server.Tests";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } =
      new NullFileProvider();
  }
}
