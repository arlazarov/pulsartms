using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Fleet;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Infrastructure.Integrations.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Server.Tests.Messaging;

// Delivery notifications as Meta sends them: signed with the carrier's app
// secret, repeated, out of order, about old attempts and other numbers.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class DriverMessagingWebhookTests
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

    var at = DateTimeOffset.FromUnixTimeSeconds(1790000000).UtcDateTime;
    f.Refresh.Time.UtcNow = at.AddHours(1);
    Assert.Equal(
      new DriverTextReadiness(true, at.AddHours(24)),
      await f.Delivery().ReadinessAsync("+15558234327", default)
    );
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

  // One notification, one transaction: when its commit fails (the webhook
  // answers an error) nothing of it is kept, and the provider's retry of
  // the whole notification records all of it once. Nothing is sent, and
  // the fuel plan's owner hears of its status only after the commit.
  [Fact]
  public async Task ARetryAfterAFailedCommitSettlesEverythingOnce()
  {
    await using var f = await Fixture.CreateAsync();
    await f.DriverAsync("+15558234327");
    var sent = await f.MessageAsync("wamid.plan");
    var inbound = Inbound(new { type = "text", text = new { body = "ok" } });
    var status = Status("wamid.plan", "delivered", 20);

    f.Probe.FailNextSave = true;
    await Assert.ThrowsAsync<DbUpdateException>(() => f.PostAsync(inbound));
    f.Probe.FailNextSave = true;
    await Assert.ThrowsAsync<DbUpdateException>(() => f.PostAsync(status));
    Assert.Empty(await f.Db.ConversationMessages.AsNoTracking().ToListAsync());
    Assert.Equal(DriverMessageStatuses.Accepted, await f.StatusAsync(sent));
    Assert.Empty(f.Notified);

    Assert.Equal(200, await f.PostAsync(inbound));
    Assert.Equal(200, await f.PostAsync(status));
    Assert.Equal(200, await f.PostAsync(inbound));
    Assert.Equal(200, await f.PostAsync(status));

    Assert.Equal(DriverMessageStatuses.Delivered, await f.StatusAsync(sent));
    var truck = await f
      .Db.DriverMessages.AsNoTracking()
      .Select(x => x.TruckId)
      .SingleAsync();
    Assert.Equal(
      (Domain.Entities.Company.Amf, truck),
      Assert.Single(f.Notified)
    );
    // Nothing went out twice, or at all: no provider call, no new fuel
    // attempt, no reply, and the driver's message recorded once.
    Assert.Equal(0, f.ProviderCalls);
    Assert.Equal(
      (1, 1),
      (
        await f.Db.DriverMessages.CountAsync(),
        await f.Db.DriverMessages.MaxAsync(x => x.Attempt)
      )
    );
    var messages = await f.Db.ConversationMessages.AsNoTracking().ToListAsync();
    Assert.Equal(MessageDirections.Inbound, Assert.Single(messages).Direction);
  }

  // A fuel plan went from the number it records. After the carrier moves
  // to another number, a delayed status for an attempt from the old number
  // cannot move it, even under a provider id the new number reuses; an
  // attempt recorded before numbers were kept is moved by nothing. Statuses
  // addressed to the old number are dropped before any of this
  // (AnUnsignedOrMisaddressedNotificationChangesNothing).
  [Fact]
  public async Task AFuelPlansStatusMovesOnlyUnderTheNumberItWentFrom()
  {
    await using var f = await Fixture.CreateAsync();
    var old = await f.MessageAsync("wamid.reused", number: "111111");
    var legacy = await f.MessageAsync("wamid.legacy", number: null);
    var current = await f.MessageAsync("wamid.current");

    Assert.Equal(200, await f.PostAsync(Status("wamid.reused", "read", 10)));
    Assert.Equal(200, await f.PostAsync(Status("wamid.legacy", "read", 10)));
    Assert.Equal(200, await f.PostAsync(Status("wamid.current", "read", 10)));

    Assert.Equal(DriverMessageStatuses.Accepted, await f.StatusAsync(old));
    Assert.Equal(DriverMessageStatuses.Accepted, await f.StatusAsync(legacy));
    Assert.Equal(DriverMessageStatuses.Read, await f.StatusAsync(current));
    Assert.Single(f.Notified);
  }

  // The reply window is the conversation's under the current number: a
  // driver who wrote to another number has not opened it.
  [Fact]
  public async Task AWindowUnderAnotherNumberIsNotThisOnes()
  {
    await using var f = await Fixture.CreateAsync();
    var at = DateTimeOffset.FromUnixTimeSeconds(1790000000).UtcDateTime;
    f.Db.Conversations.Add(
      new Conversation
      {
        Id = Guid.NewGuid(),
        CompanyId = Domain.Entities.Company.Amf,
        Channel = DriverMessageChannels.WhatsApp,
        BusinessNumberId = "111111",
        Participant = "+15558234327",
        LastInboundAt = at,
        LastMessageAt = at,
      }
    );
    await f.Db.SaveChangesAsync();
    f.Refresh.Time.UtcNow = at.AddHours(1);

    Assert.Equal(
      new DriverTextReadiness(true, null),
      await f.Delivery().ReadinessAsync("+15558234327", default)
    );
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
      new VerifyDriverMessagingWebhookQuery(
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
        new VerifyDriverMessagingWebhookQuery(
          "amfcarrier",
          "subscribe",
          "verify-other",
          "42"
        ),
        new VerifyDriverMessagingWebhookQuery(
          "other",
          "subscribe",
          "verify-amf",
          "42"
        ),
        new VerifyDriverMessagingWebhookQuery(
          "amfcarrier",
          "subscribe",
          "verify-amf",
          "<b>"
        ),
        new VerifyDriverMessagingWebhookQuery(
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
    public required SaveFailureProbe Probe { get; init; }

    // What the requesting module was told after each commit.
    public List<(Guid Company, Guid Truck)> Notified { get; } = [];
    public Infrastructure.Persistence.AppDbContext Db => Refresh.Db;
    public ICurrentCompany Company =>
      Refresh.Services.GetRequiredService<ICurrentCompany>();

    public static async Task<Fixture> CreateAsync()
    {
      var probe = new SaveFailureProbe();
      var f = new Fixture
      {
        Probe = probe,
        Refresh = await PlanningRefreshFixture.CreateAsync(services =>
          services.ConfigureDbContext<Infrastructure.Persistence.AppDbContext>(
            options => options.AddInterceptors(probe)
          )
        ),
      };
      f.Db.Companies.AddRange(
        new Company { Id = Domain.Entities.Company.Amf, Key = "amfcarrier" },
        new Company { Id = Other, Key = "other" }
      );
      await f.Db.SaveChangesAsync();
      return f;
    }

    // Every call the provider adapter would make to Meta; receiving a
    // notification must never make one.
    public int ProviderCalls { get; private set; }

    private WhatsAppCloudMessaging Provider() =>
      new(
        new HttpClient(new Counting(() => ProviderCalls++)),
        new Credentials(Company),
        new ConfigurationBuilder().Build()
      );

    public DriverMessagingWebhookHandlers Handler() =>
      new(
        Db,
        Provider(),
        Company,
        new InboxRecorder(Db, TimeProvider.System),
        Events,
        [new Observer(Notified)]
      );

    public DriverTextDelivery Delivery() =>
      new(
        Db,
        Provider(),
        Company,
        Refresh.Time,
        NullLogger<DriverTextDelivery>.Instance
      );

    public MessagingEvents Events { get; } = new();

    public async Task<Guid> MessageAsync(
      string providerId,
      string status = DriverMessageStatuses.Accepted,
      string? number = "123456"
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
        BusinessNumberId = number,
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
      var received = await Handler()
        .Handle(
          new ReceiveDriverMessagesCommand(
            key,
            signature,
            new MemoryStream(body)
          ),
          default
        );
      return received.StatusCode;
    }

    public ValueTask DisposeAsync() => Refresh.DisposeAsync();
  }

  private sealed class Observer(List<(Guid, Guid)> notified)
    : IDriverTextObserver
  {
    public void Changed(
      Guid company,
      IReadOnlyCollection<DriverMessage> attempts
    ) => notified.AddRange(attempts.Select(x => (company, x.TruckId)));
  }

  private sealed class Counting(Action called) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      called();
      return Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.InternalServerError)
      );
    }
  }
}
