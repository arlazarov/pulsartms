using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Application.Models;
using Domain.Entities;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Messaging;

// A company's driver inbox for the inbox and notice tests: dispatchers,
// driver messages recorded through the webhook's recorder, and the
// handlers as a given dispatcher calls them.
internal static class InboxScenario
{
  internal static readonly DateTime Start = new(
    2026,
    9,
    21,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  internal static async Task<IReadOnlyList<ConversationSummary>> InboxAsync(
    DispatchSyncFixture f,
    Guid user
  ) =>
    (await Handlers(f, user).Handle(new GetInboxQuery(false), default))
      .Response!
      .Conversations;

  internal static async Task<(Guid, Guid)> UsersAsync(DispatchSyncFixture f)
  {
    var me = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "me",
      Name = "Me",
      Email = "me@example.invalid",
    };
    var colleague = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "colleague",
      Name = "Colleague",
      Email = "c@example.invalid",
    };
    f.Db.Users.AddRange(me, colleague);
    await f.Db.SaveChangesAsync();
    return (me.Id, colleague.Id);
  }

  // One minute apart from Start, from one number.
  internal static async Task<Guid> ReceiveAsync(
    DispatchSyncFixture f,
    string phone,
    int count
  )
  {
    f.Db.ChangeTracker.Clear();
    await new InboxRecorder(f.Db, TimeProvider.System).RecordAsync(
      DriverMessageChannels.WhatsApp,
      "123456",
      [
        .. Enumerable
          .Range(0, count)
          .Select(i => new DriverMessageInboundEvent(phone, Start.AddMinutes(i))
          {
            ProviderMessageId = $"wamid.{phone}.{i}",
            Text = $"message {i}",
          }),
      ],
      default
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return await f
      .Db.Conversations.AsNoTracking()
      .Where(x => x.Participant == phone)
      .Select(x => x.Id)
      .SingleAsync();
  }

  internal static Task ReadAsync(
    DispatchSyncFixture f,
    Guid user,
    Guid conversation,
    long revision
  ) =>
    Handlers(f, user)
      .Handle(new MarkConversationReadCommand(conversation, revision), default);

  internal static async Task RecordAsync(
    DispatchSyncFixture f,
    string phone,
    DateTime at,
    string id
  )
  {
    f.Db.ChangeTracker.Clear();
    await new InboxRecorder(f.Db, TimeProvider.System).RecordAsync(
      DriverMessageChannels.WhatsApp,
      "123456",
      [
        new DriverMessageInboundEvent(phone, at)
        {
          ProviderMessageId = $"wamid.{phone}.{id}",
          Text = id,
        },
      ],
      default
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
  }

  internal static async Task<UnreadNotice> NoticeAsync(
    DispatchSyncFixture f,
    Guid user
  ) =>
    (
      await Handlers(f, user).Handle(new GetUnreadNoticeQuery(), default)
    ).Response!;

  internal static InboxAndConversation Handlers(
    DispatchSyncFixture f,
    Guid user,
    DriverScope? scope = null
  )
  {
    f.Db.ChangeTracker.Clear();
    var identity = f
      .Db.Users.AsNoTracking()
      .Where(x => x.Id == user)
      .Select(x => x.IdentityUserId)
      .Single();
    return new(
      new InboxHandlers(
        f.Db,
        new Caller(identity),
        new ConversationReadMarkers(f.Db),
        new TestDriverScope(scope),
        TimeProvider.System
      ),
      new ConversationHandlers(
        f.Db,
        new Caller(identity),
        new TestCompany(),
        new MessagingEvents(),
        TimeProvider.System
      )
    );
  }

  internal sealed class InboxAndConversation(
    InboxHandlers inbox,
    ConversationHandlers conversation
  )
  {
    public Task<RequestResponse<InboxView>> Handle(
      GetInboxQuery q,
      CancellationToken ct
    ) => inbox.Handle(q, ct);

    public Task<RequestResponse<UnreadNotice>> Handle(
      GetUnreadNoticeQuery q,
      CancellationToken ct
    ) => inbox.Handle(q, ct);

    public Task<RequestResponse<bool>> Handle(
      MarkConversationReadCommand q,
      CancellationToken ct
    ) => inbox.Handle(q, ct);

    public Task<RequestResponse<ConversationView>> Handle(
      GetConversationQuery q,
      CancellationToken ct
    ) => conversation.Handle(q, ct);
  }

  internal sealed class Caller(string identity) : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => identity;
  }
}
