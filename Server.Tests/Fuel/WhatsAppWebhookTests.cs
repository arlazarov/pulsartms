using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Services.Messaging;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Fleet;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Infrastructure.Integrations.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Fuel;

// Delivery notifications as Meta sends them: signed with the carrier's app
// secret, repeated, out of order, about old attempts and other numbers.
[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class WhatsAppWebhookTests
{
  private static readonly Guid Other = new(
    "b0f0b0f0-0000-4000-8000-000000000002"
  );

  [Fact]
  public async Task AStatusMovesOnlyItsOwnMessageAndNeverBack()
  {
    await using var f = await Fixture.CreateAsync();
    var old = await f.MessageAsync("wamid.old", DriverMessageStatuses.Failed);
    var current = await f.MessageAsync("wamid.new");

    // Read arrives before delivered, then both arrive again; a late
    // delivery for the failed old attempt changes nothing.
    Assert.Equal(200, await f.PostAsync(Status("wamid.new", "read", 30)));
    Assert.Equal(200, await f.PostAsync(Status("wamid.new", "delivered", 20)));
    Assert.Equal(200, await f.PostAsync(Status("wamid.new", "read", 30)));
    Assert.Equal(200, await f.PostAsync(Status("wamid.new", "failed", 40)));
    Assert.Equal(200, await f.PostAsync(Status("wamid.old", "delivered", 50)));

    Assert.Equal(DriverMessageStatuses.Read, await f.StatusAsync(current));
    Assert.Equal(DriverMessageStatuses.Failed, await f.StatusAsync(old));
  }

  [Fact]
  public async Task AFailureBeforeDeliveryIsKeptAsANumber()
  {
    await using var f = await Fixture.CreateAsync();
    await f.MessageAsync("wamid.1");
    await f.PostAsync(Status("wamid.1", "sent", 10));
    await f.PostAsync(Status("wamid.1", "failed", 20, code: 131047));

    var row = await f.Db.DriverMessages.AsNoTracking().SingleAsync();
    Assert.Equal(DriverMessageStatuses.Failed, row.Status);
    Assert.Equal(131047, row.ErrorCode);
  }

  [Fact]
  public async Task AnUnsignedOrMisaddressedNotificationChangesNothing()
  {
    await using var f = await Fixture.CreateAsync();
    var message = await f.MessageAsync("wamid.1");

    Assert.Equal(
      401,
      await f.PostAsync(Status("wamid.1", "delivered", 10), secret: "wrong")
    );
    Assert.Equal(
      200,
      await f.PostAsync(Status("wamid.1", "delivered", 10, number: "999"))
    );
    Assert.Equal(DriverMessageStatuses.Accepted, await f.StatusAsync(message));
  }

  [Fact]
  public async Task AnotherCarriersMessageIsOutOfReach()
  {
    await using var f = await Fixture.CreateAsync();
    Guid theirs;
    using (f.Company.As(Other))
      theirs = await f.MessageAsync("wamid.shared");

    // Signed for AMF, about a message id another carrier holds.
    Assert.Equal(200, await f.PostAsync(Status("wamid.shared", "read", 10)));
    // Signed with AMF's secret, sent to the other carrier's address.
    Assert.Equal(
      401,
      await f.PostAsync(Status("wamid.shared", "read", 10), key: "other")
    );
    using (f.Company.As(Other))
      Assert.Equal(DriverMessageStatuses.Accepted, await f.StatusAsync(theirs));
  }

  // Since the inbox (2026-09-23) what drivers write is kept: once, however
  // often the notification arrives, and the window still opens.
  [Fact]
  public async Task AnInboundMessageOpensTheWindowAndIsRecordedOnce()
  {
    await using var f = await Fixture.CreateAsync();
    var driver = await f.DriverAsync("+15558234327");
    using var listening = f.Events.Subscribe(Domain.Entities.Company.Amf);
    var body = Inbound(new { type = "text", text = new { body = "ok" } });

    Assert.Equal(200, await f.PostAsync(body));
    Assert.Equal(200, await f.PostAsync(body));

    var window = await f.Db.DriverMessagingWindows.SingleAsync();
    Assert.Equal("+15558234327", window.Phone);
    var message = await f.Db.ConversationMessages.AsNoTracking().SingleAsync();
    Assert.Equal(
      ("in", "text", "ok", "wamid.in", "123456"),
      (
        message.Direction,
        message.Kind,
        message.Body,
        message.ProviderMessageId,
        message.BusinessNumberId
      )
    );
    var conversation = await f.Db.Conversations.AsNoTracking().SingleAsync();
    Assert.Equal(
      (
        driver,
        "ok",
        DateTimeOffset.FromUnixTimeSeconds(1790000000).UtcDateTime
      ),
      (
        conversation.DriverId,
        conversation.LastPreview,
        conversation.LastInboundAt
      )
    );
    Assert.True(listening.Reader.TryRead(out var change));
    Assert.Equal(conversation.Id, change.ConversationId);
    Assert.False(listening.Reader.TryRead(out _));
  }

  // The same provider message id under another carrier's number is that
  // carrier's own message, never a duplicate of this one.
  [Fact]
  public async Task TheSameMessageIdUnderAnotherBusinessNumberIsAnotherMessage()
  {
    await using var f = await Fixture.CreateAsync();
    var body = Inbound(new { type = "text", text = new { body = "hi" } });
    Assert.Equal(200, await f.PostAsync(body));
    Assert.Equal(
      200,
      await f.PostAsync(
        Inbound(new { type = "text", text = new { body = "hi" } }, "654321"),
        "secret-other",
        "other"
      )
    );

    Assert.Single(await f.Db.ConversationMessages.AsNoTracking().ToListAsync());
    using (f.Company.As(Other))
      Assert.Equal(
        "654321",
        (
          await f.Db.ConversationMessages.AsNoTracking().SingleAsync()
        ).BusinessNumberId
      );
  }

  [Fact]
  public async Task AFileWaitsToBeCopiedAndOtherKindsAreRecordedWithoutGuessing()
  {
    await using var f = await Fixture.CreateAsync();
    Assert.Equal(
      200,
      await f.PostAsync(
        Inbound(
          new
          {
            type = "document",
            document = new
            {
              id = "1234567890",
              mime_type = "application/pdf",
              sha256 = "abc",
              filename = "bol 1407.pdf",
              caption = "BOL",
            },
          }
        )
      )
    );
    Assert.Equal(
      200,
      await f.PostAsync(
        Inbound(
          new { type = "location", location = new { latitude = 1 } },
          id: "wamid.loc"
        )
      )
    );

    var messages = await f
      .Db.ConversationMessages.AsNoTracking()
      .OrderBy(x => x.CreatedAt)
      .ToListAsync();
    Assert.Equal(("file", "BOL"), (messages[0].Kind, messages[0].Body));
    Assert.Equal(
      ("unsupported", "location"),
      (messages[1].Kind, messages[1].Body)
    );
    var file = await f.Db.MessageAttachments.AsNoTracking().SingleAsync();
    Assert.Equal(
      ("1234567890", "application/pdf", "bol 1407.pdf", "pending"),
      (file.ProviderMediaId, file.DeclaredType, file.OriginalName, file.State)
    );
    Assert.True(file.MediaExpiresAt > DateTime.UtcNow.AddDays(6));
  }

  // Two drivers share the number: the conversation stays unmatched rather
  // than guessing which of them wrote.
  [Fact]
  public async Task AnAmbiguousNumberMatchesNoDriver()
  {
    await using var f = await Fixture.CreateAsync();
    await f.DriverAsync("+15558234327");
    await f.DriverAsync("+15558234327");

    Assert.Equal(
      200,
      await f.PostAsync(
        Inbound(new { type = "text", text = new { body = "x" } })
      )
    );

    Assert.Null(
      (await f.Db.Conversations.AsNoTracking().SingleAsync()).DriverId
    );
  }

  [Fact]
  public async Task AReplysStatusMovesOnlyForwardUnderItsOwnNumber()
  {
    await using var f = await Fixture.CreateAsync();
    Assert.Equal(
      200,
      await f.PostAsync(
        Inbound(new { type = "text", text = new { body = "x" } })
      )
    );
    var conversation = await f.Db.Conversations.AsNoTracking().SingleAsync();
    f.Db.ConversationMessages.Add(
      new ConversationMessage
      {
        Id = Guid.NewGuid(),
        CompanyId = Domain.Entities.Company.Amf,
        ConversationId = conversation.Id,
        Channel = DriverMessageChannels.WhatsApp,
        BusinessNumberId = "123456",
        Direction = MessageDirections.Outbound,
        Kind = ConversationMessageKinds.Text,
        Body = "On my way",
        ProviderMessageId = "wamid.reply",
        Status = DriverMessageStatuses.Accepted,
      }
    );
    await f.Db.SaveChangesAsync();

    Assert.Equal(200, await f.PostAsync(Status("wamid.reply", "read", 30)));
    Assert.Equal(
      200,
      await f.PostAsync(Status("wamid.reply", "delivered", 20))
    );
    Assert.Equal(
      200,
      await f.PostAsync(
        Status("wamid.reply", "failed", 40, number: "654321"),
        "secret-other",
        "other"
      )
    );

    Assert.Equal(
      DriverMessageStatuses.Read,
      (
        await f
          .Db.ConversationMessages.AsNoTracking()
          .SingleAsync(x => x.ProviderMessageId == "wamid.reply")
      ).Status
    );
  }

  private static byte[] Inbound(
    object content,
    string number = "123456",
    string id = "wamid.in"
  )
  {
    var message = JsonSerializer.SerializeToNode(content)!.AsObject();
    message["from"] = "15558234327";
    message["id"] = id;
    message["timestamp"] = "1790000000";
    return Envelope(number, new { messages = new[] { message } });
  }

  [Fact]
  public async Task SubscriptionIsConfirmedOnlyForTheCarriersToken()
  {
    await using var f = await Fixture.CreateAsync();
    var handler = f.Handler();
    var ok = await handler.Handle(
      new VerifyWhatsAppWebhookQuery(
        "amfcarrier",
        "subscribe",
        "verify-amf",
        "42"
      ),
      default
    );
    Assert.Equal("42", ok.Response);
    foreach (
      var wrong in new[]
      {
        new VerifyWhatsAppWebhookQuery(
          "amfcarrier",
          "subscribe",
          "verify-other",
          "42"
        ),
        new VerifyWhatsAppWebhookQuery(
          "other",
          "subscribe",
          "verify-amf",
          "42"
        ),
        new VerifyWhatsAppWebhookQuery(
          "amfcarrier",
          "subscribe",
          "verify-amf",
          "<b>"
        ),
        new VerifyWhatsAppWebhookQuery(
          "missing",
          "subscribe",
          "verify-amf",
          "42"
        ),
      }
    )
      Assert.Equal(403, (await handler.Handle(wrong, default)).StatusCode);
  }

  private static byte[] Status(
    string id,
    string status,
    long second,
    int? code = null,
    string number = "123456"
  ) =>
    Envelope(
      number,
      new
      {
        statuses = new object[]
        {
          code is { } error
            ? new
            {
              id,
              status,
              timestamp = (1790000000 + second).ToString(),
              recipient_id = "15558234327",
              errors = new[] { new { code = error, title = "x" } },
            }
            : new
            {
              id,
              status,
              timestamp = (1790000000 + second).ToString(),
              recipient_id = "15558234327",
            },
        },
      }
    );

  private static byte[] Envelope(string number, object value)
  {
    var json = JsonSerializer.SerializeToNode(value)!.AsObject();
    json["messaging_product"] = "whatsapp";
    json["metadata"] = JsonSerializer.SerializeToNode(
      new { display_phone_number = "15550000000", phone_number_id = number }
    );
    return JsonSerializer.SerializeToUtf8Bytes(
      new
      {
        @object = "whatsapp_business_account",
        entry = new[]
        {
          new
          {
            id = "waba",
            changes = new[] { new { field = "messages", value = json } },
          },
        },
      }
    );
  }

  private sealed class Credentials(ICurrentCompany company)
    : IIntegrationCredentials
  {
    public Task<IntegrationCredentialValues> GetAsync(
      string provider,
      CancellationToken ct
    )
    {
      var who = company.Id == Company.Amf ? "amf" : "other";
      return Task.FromResult(
        new IntegrationCredentialValues(
          new Dictionary<string, string>
          {
            ["phoneNumberId"] = who == "amf" ? "123456" : "654321",
            ["accessToken"] = "token-" + who,
            ["appSecret"] = "secret-" + who,
            ["verifyToken"] = "verify-" + who,
          }
        )
      );
    }
  }

  private sealed class Fixture : IAsyncDisposable
  {
    public required PlanningRefreshFixture Refresh { get; init; }
    public Infrastructure.Persistence.AppDbContext Db => Refresh.Db;
    public ICurrentCompany Company =>
      Refresh.Services.GetRequiredService<ICurrentCompany>();

    public static async Task<Fixture> CreateAsync()
    {
      var f = new Fixture
      {
        Refresh = await PlanningRefreshFixture.CreateAsync(),
      };
      f.Db.Companies.AddRange(
        new Company { Id = Domain.Entities.Company.Amf, Key = "amfcarrier" },
        new Company { Id = Other, Key = "other" }
      );
      await f.Db.SaveChangesAsync();
      return f;
    }

    public WhatsAppWebhookHandlers Handler() =>
      new(
        Db,
        new WhatsAppCloudMessaging(
          new HttpClient(),
          new Credentials(Company),
          new ConfigurationBuilder().Build()
        ),
        Company,
        Refresh.Services.GetRequiredService<PlanningSummaryCache>(),
        new InboxRecorder(Db, TimeProvider.System),
        Events
      );

    public MessagingEvents Events { get; } = new();

    public async Task<Guid> MessageAsync(
      string providerId,
      string status = DriverMessageStatuses.Accepted
    )
    {
      var truckRow = new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = Guid.NewGuid().ToString("N"),
        UnitNumber = Guid.NewGuid().ToString("N")[..8],
        IsActive = true,
      };
      var driverRow = new Driver
      {
        Id = Guid.NewGuid(),
        ExternalId = Guid.NewGuid().ToString("N"),
        Name = "Driver",
        IsActive = true,
      };
      var message = new DriverMessage
      {
        Id = Guid.NewGuid(),
        DriverId = driverRow.Id,
        TruckId = truckRow.Id,
        DispatchId = Guid.NewGuid(),
        Channel = DriverMessageChannels.WhatsApp,
        Recipient = "+15558234327",
        Text = "Fuel for this shift",
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        Attempt = 1,
        ProviderMessageId = providerId,
        Status = status,
      };
      Db.AddRange(truckRow, driverRow, message);
      await Db.SaveChangesAsync();
      Db.ChangeTracker.Clear();
      return message.Id;
    }

    public async Task<Guid> DriverAsync(string whatsApp)
    {
      var driver = new Driver
      {
        Id = Guid.NewGuid(),
        ExternalId = Guid.NewGuid().ToString("N"),
        Name = "Driver",
        IsActive = true,
        WhatsAppPhone = whatsApp,
      };
      Db.Drivers.Add(driver);
      await Db.SaveChangesAsync();
      Db.ChangeTracker.Clear();
      return driver.Id;
    }

    public Task<string> StatusAsync(Guid id) =>
      Db
        .DriverMessages.AsNoTracking()
        .Where(x => x.Id == id)
        .Select(x => x.Status)
        .SingleAsync();

    public async Task<int> PostAsync(
      byte[] body,
      string secret = "secret-amf",
      string key = "amfcarrier"
    )
    {
      var signature =
        "sha256="
        + Convert
          .ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)
          )
          .ToLowerInvariant();
      Db.ChangeTracker.Clear();
      var result = await Handler()
        .Handle(
          new ReceiveWhatsAppWebhookCommand(
            key,
            signature,
            new MemoryStream(body)
          ),
          default
        );
      return result.StatusCode;
    }

    public ValueTask DisposeAsync() => Refresh.DisposeAsync();
  }
}
