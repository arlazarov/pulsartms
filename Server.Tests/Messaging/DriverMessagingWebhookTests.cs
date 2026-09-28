using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Application.Features.Messaging.Audit;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Interfaces;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Fleet;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Infrastructure.Integrations.WhatsApp;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Tests.Support;

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

  // Where a notification stops is counted and, for a refusal or a drop,
  // logged once by company key and reason - the silent case was a signed
  // notification for another business number, answered 200 and dropped.
  // No number, message id or text reaches the log.
  [Fact]
  public async Task EachWayANotificationStopsIsCountedAndNamed()
  {
    await using var f = await Fixture.CreateAsync();
    await f.MessageAsync("wamid.1");
    var before = PerformanceStages.Snapshot();
    long Counted(string outcome) =>
      (
        PerformanceStages
          .Snapshot()
          .GetValueOrDefault($"driver-messaging-webhook/{outcome}")
          ?.Items ?? 0
      )
      - (
        before.GetValueOrDefault($"driver-messaging-webhook/{outcome}")?.Items
        ?? 0
      );

    Assert.Equal(
      401,
      await f.PostAsync(Status("wamid.1", "delivered", 10), secret: "wrong")
    );
    Assert.Equal(
      404,
      await f.PostAsync(Status("wamid.1", "delivered", 10), key: "nobody")
    );
    Assert.Equal(
      200,
      await f.PostAsync(Status("wamid.1", "delivered", 10, number: "999"))
    );
    Assert.Equal(200, await f.PostAsync(Status("wamid.1", "delivered", 10)));

    // Other tests count too, in parallel: only increases are asserted.
    Assert.True(Counted("signature") >= 1);
    Assert.True(Counted("unknown-company") >= 1);
    Assert.True(Counted("other-number-changes") >= 1);
    Assert.True(Counted("accepted") >= 2);
    Assert.Equal(
      [
        "Driver messaging webhook refused for amfcarrier: signature",
        "Driver messaging webhook refused for nobody: unknown-company",
        "Driver messaging webhook for amfcarrier dropped 1 changes "
          + "addressed to another business number",
      ],
      f.Logger.Lines
    );
    Assert.All(f.Logger.Lines, x => Assert.DoesNotContain("wamid", x));
    Assert.All(f.Logger.Lines, x => Assert.DoesNotContain("999", x));
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
    // Still written for revisions from before 2026-09-24, which read it.
    var legacy = await f.Db.DriverMessagingWindows.AsNoTracking().SingleAsync();
    Assert.Equal(("+15558234327", at), (legacy.Phone, legacy.LastInboundAt));
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
  // driver who wrote to another number has not opened it, and a legacy
  // window row, which names no number, opens nothing.
  [Fact]
  public async Task AWindowUnderAnotherNumberIsNotThisOnes()
  {
    await using var f = await Fixture.CreateAsync();
    var at = DateTimeOffset.FromUnixTimeSeconds(1790000000).UtcDateTime;
    f.Db.DriverMessagingWindows.Add(
      new DriverMessagingWindow
      {
        Id = Guid.NewGuid(),
        CompanyId = Domain.Entities.Company.Amf,
        Channel = DriverMessageChannels.WhatsApp,
        Phone = "+15558234327",
        LastInboundAt = at,
      }
    );
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

  // The driver taps "I'm available" on the contact request: a reply like
  // any other, which opens the reply window.
  [Fact]
  public async Task AQuickReplyTapIsTheDriverWriting()
  {
    await using var f = await Fixture.CreateAsync();
    Assert.Equal(
      200,
      await f.PostAsync(
        Inbound(
          new
          {
            type = "button",
            button = new { text = "I'm available", payload = "I'm available" },
          },
          id: "wamid.tap"
        )
      )
    );

    var message = await f.Db.ConversationMessages.AsNoTracking().SingleAsync();
    Assert.Equal(("text", "I'm available"), (message.Kind, message.Body));
    Assert.NotNull(
      (await f.Db.Conversations.AsNoTracking().SingleAsync()).LastInboundAt
    );
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
    // WhatsApp's receipt says the driver read the reply; it reads nothing
    // for any dispatcher.
    Assert.Empty(await f.Db.ConversationReads.AsNoTracking().ToListAsync());
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

  // Audit F27: a status can arrive before the provider id it names is
  // saved - here while the send is still waiting for the provider's answer,
  // as another request. It was dropped, so an early failure left the
  // attempt showing accepted. It is kept and applied when the id is saved.
  [Fact]
  public async Task AStatusBeforeTheSendIsAnsweredIsAppliedWhenTheIdIsSaved()
  {
    await using var f = await Fixture.CreateAsync();
    var (driver, truck) = await f.RecipientAsync("+15558234327");
    var transport = new FakeDriverMessaging();
    transport.During = async () =>
      Assert.Equal(
        200,
        await f.PostFromAnotherRequestAsync(
          Status("wamid.1", "failed", 10, code: 131047)
        )
      );
    var attempt = new DriverMessage
    {
      DriverId = driver,
      TruckId = truck,
      DispatchId = Guid.NewGuid(),
      Recipient = "+15558234327",
      Text = "Fuel for this shift",
      IdempotencyKey = Guid.NewGuid().ToString("N"),
    };

    var outcome = await f.Delivery(transport)
      .SendAsync(attempt, false, _ => Task.FromResult(true), default);

    Assert.Equal(DriverTextResult.Accepted, outcome.Result);
    f.Db.ChangeTracker.Clear();
    var row = await f.Db.DriverMessages.AsNoTracking().SingleAsync();
    Assert.Equal(
      ("wamid.1", DriverMessageStatuses.Failed, (int?)131047),
      (row.ProviderMessageId, row.Status, row.ErrorCode)
    );
  }

  // The provider repeats a notification it thinks went unanswered: the
  // early status is kept once and applied once.
  [Fact]
  public async Task AnEarlyStatusRepeatedIsKeptOnceAndAppliedOnce()
  {
    await using var f = await Fixture.CreateAsync();
    var (driver, truck) = await f.RecipientAsync("+15558234327");
    var transport = new FakeDriverMessaging();
    var keptDuringSend = -1;
    transport.During = async () =>
    {
      for (var i = 0; i < 2; i++)
        await f.PostFromAnotherRequestAsync(
          Status("wamid.1", "failed", 10, code: 131047)
        );
      await using var scope = f.Refresh.NewScope();
      keptDuringSend = await scope
        .ServiceProvider.GetRequiredService<AppDbContext>()
        .PendingDeliveryStatuses.CountAsync();
    };

    await f.Delivery(transport)
      .SendAsync(
        f.Attempt(driver, truck),
        false,
        _ => Task.FromResult(true),
        default
      );

    Assert.Equal(1, keptDuringSend);
    Assert.Equal(
      DriverMessageStatuses.Failed,
      (await f.Db.DriverMessages.AsNoTracking().SingleAsync()).Status
    );
    Assert.Empty(
      await f.Db.PendingDeliveryStatuses.AsNoTracking().ToListAsync()
    );
  }

  // Another carrier's early status for the same provider id is that
  // carrier's: it waits under its own number and touches nothing here.
  [Fact]
  public async Task AnotherCarriersEarlyStatusDoesNotTouchThisSend()
  {
    await using var f = await Fixture.CreateAsync();
    var (driver, truck) = await f.RecipientAsync("+15558234327");
    var transport = new FakeDriverMessaging();
    transport.During = async () =>
      Assert.Equal(
        200,
        await f.PostFromAnotherRequestAsync(
          Status("wamid.1", "failed", 10, code: 131047, number: "654321"),
          key: "other",
          secret: "secret-other"
        )
      );

    await f.Delivery(transport)
      .SendAsync(
        f.Attempt(driver, truck),
        false,
        _ => Task.FromResult(true),
        default
      );

    Assert.Equal(
      DriverMessageStatuses.Accepted,
      (await f.Db.DriverMessages.AsNoTracking().SingleAsync()).Status
    );
    Assert.Empty(
      await f.Db.PendingDeliveryStatuses.AsNoTracking().ToListAsync()
    );
    using (f.Company.As(Other))
      Assert.Single(
        await f.Db.PendingDeliveryStatuses.AsNoTracking().ToListAsync()
      );
  }

  // A dispatcher's reply: its status arrives before the outbox saves the
  // provider id; it applies when the id is saved.
  [Fact]
  public async Task AReplysEarlyStatusIsAppliedWhenTheOutboxSavesItsId()
  {
    await using var f = await Fixture.CreateAsync();
    var (conversation, reply) = await f.ReplyInFlightAsync();
    Assert.Equal(
      200,
      await f.PostAsync(Status("wamid.reply", "delivered", 20))
    );

    await using (var scope = f.Refresh.NewScope())
      Assert.True(
        await new OutboxRecords(f.Events, f.Refresh.Time).FinishAsync(
          scope.ServiceProvider,
          reply,
          conversation,
          1,
          DriverMessageStatuses.Sending,
          DriverMessageStatuses.Accepted,
          "wamid.reply",
          null,
          default
        )
      );

    var row = await f
      .Db.ConversationMessages.AsNoTracking()
      .SingleAsync(x => x.Id == reply);
    Assert.Equal(
      ("wamid.reply", DriverMessageStatuses.Delivered),
      (row.ProviderMessageId, row.Status)
    );
    Assert.Empty(
      await f.Db.PendingDeliveryStatuses.AsNoTracking().ToListAsync()
    );
  }

  // The provider's answer came late - the attempt was marked unknown or
  // another worker held it - and its status came before that: it applies
  // when the late answer saves the id.
  [Fact]
  public async Task AReplysEarlyStatusIsAppliedWhenALateAnswerSavesItsId()
  {
    await using var f = await Fixture.CreateAsync();
    var (conversation, reply) = await f.ReplyInFlightAsync();
    Assert.Equal(200, await f.PostAsync(Status("wamid.reply", "read", 20)));

    await using (var scope = f.Refresh.NewScope())
      await new OutboxRecords(f.Events, f.Refresh.Time).LateAnswerAsync(
        scope.ServiceProvider,
        reply,
        conversation,
        "wamid.reply",
        default
      );

    var row = await f
      .Db.ConversationMessages.AsNoTracking()
      .SingleAsync(x => x.Id == reply);
    Assert.Equal(
      ("wamid.reply", DriverMessageStatuses.Read),
      (row.ProviderMessageId, row.Status)
    );
    Assert.Empty(
      await f.Db.PendingDeliveryStatuses.AsNoTracking().ToListAsync()
    );
  }

  // A status for an id never saved is kept only for its lifetime: the next
  // status kept after that drops it, and it applies to nothing later.
  [Fact]
  public async Task AnEarlyStatusExpires()
  {
    await using var f = await Fixture.CreateAsync();
    var (driver, truck) = await f.RecipientAsync("+15558234327");
    Assert.Equal(
      200,
      await f.PostAsync(Status("wamid.1", "failed", 10, code: 131047))
    );
    f.Refresh.Time.Advance(
      EarlyDeliveryStatuses.Keep + TimeSpan.FromMinutes(1)
    );
    Assert.Equal(200, await f.PostAsync(Status("wamid.other", "read", 20)));

    Assert.Equal(
      ["wamid.other"],
      await f
        .Db.PendingDeliveryStatuses.AsNoTracking()
        .Select(x => x.ProviderMessageId)
        .ToListAsync()
    );
    await f.Delivery(new FakeDriverMessaging())
      .SendAsync(
        f.Attempt(driver, truck),
        false,
        _ => Task.FromResult(true),
        default
      );
    Assert.Equal(
      DriverMessageStatuses.Accepted,
      (await f.Db.DriverMessages.AsNoTracking().SingleAsync()).Status
    );
  }

  // Root's review: an early status past its lifetime is not applied even
  // when no later webhook has pruned it - the sender saving the id after
  // Keep, with nothing received meanwhile, discards it instead.
  [Fact]
  public async Task AnExpiredEarlyStatusIsNotAppliedByALateSender()
  {
    await using var f = await Fixture.CreateAsync();
    var (driver, truck) = await f.RecipientAsync("+15558234327");
    Assert.Equal(
      200,
      await f.PostAsync(Status("wamid.1", "failed", 10, code: 131047))
    );
    f.Refresh.Time.Advance(
      EarlyDeliveryStatuses.Keep + TimeSpan.FromMinutes(1)
    );

    await f.Delivery(new FakeDriverMessaging())
      .SendAsync(
        f.Attempt(driver, truck),
        false,
        _ => Task.FromResult(true),
        default
      );

    Assert.Equal(
      DriverMessageStatuses.Accepted,
      (await f.Db.DriverMessages.AsNoTracking().SingleAsync()).Status
    );
    Assert.Empty(
      await f.Db.PendingDeliveryStatuses.AsNoTracking().ToListAsync()
    );
  }

  // A carrier keeps at most PerCompany of them.
  [Fact]
  public async Task ACarrierKeepsABoundedNumber()
  {
    await using var f = await Fixture.CreateAsync();
    var early = new EarlyDeliveryStatuses(
      f.Db,
      new DeliveryStatusLocks(f.Db),
      f.Company,
      f.Refresh.Time
    );
    await using (var transaction = await f.Db.Database.BeginTransactionAsync())
    {
      await early.KeepAsync(
        DriverMessageChannels.WhatsApp,
        "123456",
        [
          .. Enumerable
            .Range(0, EarlyDeliveryStatuses.PerCompany + 5)
            .Select(i => new DriverMessageStatusEvent(
              $"wamid.{i}",
              "read",
              DateTime.UtcNow,
              null
            )),
        ],
        default
      );
      await f.Db.SaveChangesAsync();
      await transaction.CommitAsync();
    }

    Assert.Equal(
      EarlyDeliveryStatuses.PerCompany,
      await f.Db.PendingDeliveryStatuses.CountAsync()
    );
  }

  // A release overlap: the previous binary saves a provider id without
  // taking what the new one kept for it, for a fuel text and for a reply.
  // The message keeps showing accepted after the provider's failure, and
  // the audit reports each kept status; one whose id is still unknown is
  // not a finding, and one a new writer took leaves nothing to report.
  [Fact]
  public async Task AKeptStatusBehindAnIdSavedWithoutItIsReported()
  {
    await using var f = await Fixture.CreateAsync();
    await f.PostAsync(Status("wamid.1", "failed", 10, code: 131047));
    await f.PostAsync(Status("wamid.2", "failed", 11, code: 131047));
    await f.PostAsync(Status("wamid.3", "read", 12));
    var text = await f.MessageAsync("wamid.1");
    var (_, reply) = await f.ReplyInFlightAsync();
    await f
      .Db.ConversationMessages.Where(x => x.Id == reply)
      .ExecuteUpdateAsync(x =>
        x.SetProperty(m => m.ProviderMessageId, "wamid.2")
          .SetProperty(m => m.Status, DriverMessageStatuses.Accepted)
      );

    var found = (
      await new KeptStatusUnappliedRule(f.Db).ReadAsync(
        new(
          Domain.Entities.Company.Amf,
          DateTime.UtcNow,
          null,
          10,
          TimeSpan.FromMinutes(30)
        ),
        default
      )
    ).Observed;

    Assert.Equal(DriverMessageStatuses.Accepted, await f.StatusAsync(text));
    var lost = await f
      .Db.PendingDeliveryStatuses.AsNoTracking()
      .Where(x => x.ProviderMessageId != "wamid.3")
      .Select(x => x.Id)
      .ToListAsync();
    Assert.Equal(
      lost.Select(x => x.ToString()).Order(StringComparer.Ordinal),
      found.Select(x => x.EntityKey)
    );
    Assert.All(found, x => Assert.Equal("131047", x.Evidence["errorCode"]));
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

    private WhatsAppCloudMessaging Provider() => Provider(Company);

    private WhatsAppCloudMessaging Provider(ICurrentCompany company) =>
      new(
        new HttpClient(new Counting(() => ProviderCalls++)),
        new Credentials(company),
        new ConfigurationBuilder().Build()
      );

    public DriverMessagingWebhookHandlers Handler() =>
      new(
        Db,
        Provider(),
        Company,
        new InboxRecorder(Db, TimeProvider.System),
        Events,
        [new Observer(Notified)],
        new EarlyDeliveryStatuses(
          Db,
          new DeliveryStatusLocks(Db),
          Company,
          Refresh.Time
        ),
        Logger
      );

    public Recorded Logger { get; } = new();

    public DriverTextDelivery Delivery() =>
      new(
        Db,
        Provider(),
        Company,
        Refresh.Time,
        new EarlyDeliveryStatuses(
          Db,
          new DeliveryStatusLocks(Db),
          Company,
          Refresh.Time
        ),
        NullLogger<DriverTextDelivery>.Instance
      );

    public DriverTextDelivery Delivery(IDriverMessaging transport) =>
      new(
        Db,
        transport,
        Company,
        Refresh.Time,
        new EarlyDeliveryStatuses(
          Db,
          new DeliveryStatusLocks(Db),
          Company,
          Refresh.Time
        ),
        NullLogger<DriverTextDelivery>.Instance
      );

    // A driver with a truck, whose window under the business number is open.
    public async Task<(Guid Driver, Guid Truck)> RecipientAsync(string phone)
    {
      var truck = new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = Guid.NewGuid().ToString("N"),
        UnitNumber = Guid.NewGuid().ToString("N")[..8],
        IsActive = true,
      };
      var driver = new Driver
      {
        Id = Guid.NewGuid(),
        ExternalId = Guid.NewGuid().ToString("N"),
        Name = "Driver",
        IsActive = true,
        WhatsAppPhone = phone,
      };
      Db.AddRange(
        truck,
        driver,
        new Conversation
        {
          Id = Guid.NewGuid(),
          Channel = DriverMessageChannels.WhatsApp,
          BusinessNumberId = "123456",
          Participant = phone,
          LastInboundAt = Refresh.Time.GetUtcNow().UtcDateTime.AddHours(-1),
          LastMessageAt = Refresh.Time.GetUtcNow().UtcDateTime.AddHours(-1),
        }
      );
      await Db.SaveChangesAsync();
      Db.ChangeTracker.Clear();
      return (driver.Id, truck.Id);
    }

    public DriverMessage Attempt(Guid driver, Guid truck) =>
      new()
      {
        DriverId = driver,
        TruckId = truck,
        DispatchId = Guid.NewGuid(),
        Recipient = "+15558234327",
        Text = "Fuel for this shift",
        IdempotencyKey = Guid.NewGuid().ToString("N"),
      };

    // A dispatcher's reply the outbox is sending: fence 1, no provider id.
    public async Task<(Guid Conversation, Guid Reply)> ReplyInFlightAsync()
    {
      var conversation = new Conversation
      {
        Id = Guid.NewGuid(),
        Channel = DriverMessageChannels.WhatsApp,
        BusinessNumberId = "123456",
        Participant = "+15558234327",
        LastInboundAt = Refresh.Time.GetUtcNow().UtcDateTime.AddHours(-1),
        LastMessageAt = Refresh.Time.GetUtcNow().UtcDateTime.AddHours(-1),
      };
      var reply = new ConversationMessage
      {
        Id = Guid.NewGuid(),
        ConversationId = conversation.Id,
        Channel = DriverMessageChannels.WhatsApp,
        BusinessNumberId = "123456",
        Direction = "outbound",
        Kind = "text",
        Body = "On my way",
        Status = DriverMessageStatuses.Sending,
        StatusAt = Refresh.Time.GetUtcNow().UtcDateTime,
        Fence = 1,
        Attempt = 1,
      };
      Db.AddRange(conversation, reply);
      await Db.SaveChangesAsync();
      Db.ChangeTracker.Clear();
      return (conversation.Id, reply.Id);
    }

    // The webhook as another request handles it, in its own unit of work.
    public async Task<int> PostFromAnotherRequestAsync(
      byte[] body,
      string key = "amfcarrier",
      string secret = "secret-amf"
    )
    {
      await using var scope = Refresh.NewScope();
      var db =
        scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
      var signature =
        "sha256="
        + Convert
          .ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)
          )
          .ToLowerInvariant();
      // The request's own carrier, as its context sees it.
      var company = scope.ServiceProvider.GetRequiredService<ICurrentCompany>();
      var received = await new DriverMessagingWebhookHandlers(
        db,
        Provider(company),
        company,
        new InboxRecorder(db, TimeProvider.System),
        Events,
        [new Observer(Notified)],
        scope.ServiceProvider.GetRequiredService<EarlyDeliveryStatuses>(),
        Logger
      ).Handle(
        new ReceiveDriverMessagesCommand(
          key,
          signature,
          new MemoryStream(body)
        ),
        default
      );
      return received.StatusCode;
    }

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

  public sealed class Recorded : ILogger<DriverMessagingWebhookHandlers>
  {
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
      where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel,
      EventId eventId,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter
    ) => Lines.Add(formatter(state, exception));
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
