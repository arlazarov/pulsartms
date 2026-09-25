using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Microsoft.EntityFrameworkCore;
using static Server.Tests.Messaging.InboxScenario;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Messaging;

// Search reaches the whole history the company holds, not the pages shown,
// fifty at a time with nothing repeated or skipped; days are the
// dispatcher's own, across a daylight-saving change; a load is found by
// the file filed to it or by its number in the text, never by the
// driver's current assignment; another company's messages are never
// found; and a result opens as a window around it that marks nothing read.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class MessageSearchTests
{
  private const string Phone = "+15550000001";
  private static readonly DateTime Base = new(
    2026,
    9,
    1,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  [Fact]
  public async Task AWordFarBeyondThePagesShownIsFound()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var conversation = await RecordAsync(
      f,
      [
        .. Enumerable
          .Range(0, 1030)
          .Select(i =>
            (
              Base.AddMinutes(i),
              i == 40 ? "Tarp is torn on the left side" : $"message {i}"
            )
          ),
      ]
    );

    var found = await SearchAsync(
      f,
      me,
      new(conversation, "tarp", null, null, null, null)
    );

    var hit = Assert.Single(found.Hits);
    Assert.Equal(Base.AddMinutes(40), hit.SentAt);
    Assert.Contains("Tarp", hit.Snippet);
    Assert.False(found.More);
  }

  [Fact]
  public async Task PagesOfResultsSharingOneTimeRepeatAndSkipNothing()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var conversation = await RecordAsync(
      f,
      [.. Enumerable.Range(0, 120).Select(i => (Base, $"POD {i}"))]
    );

    var seen = new List<Guid>();
    MessageCursor? after = null;
    var pages = 0;
    do
    {
      var page = await SearchAsync(
        f,
        me,
        new(conversation, "pod", null, null, null, null, after)
      );
      seen.AddRange(page.Hits.Select(x => x.MessageId));
      after = page.Next;
      pages++;
    } while (after is not null);

    Assert.Equal(3, pages);
    Assert.Equal(120, seen.Distinct().Count());
    Assert.Equal(120, seen.Count);
  }

  // Toronto is four hours behind UTC in September and five after the
  // change on November 1, whose day is 25 hours long.
  [Fact]
  public async Task DaysAreTheDispatchersOwnAcrossDaylightSaving()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var conversation = await RecordAsync(
      f,
      [
        (new DateTime(2026, 9, 2, 3, 59, 0, DateTimeKind.Utc), "late on Sep 1"),
        (new DateTime(2026, 9, 2, 4, 1, 0, DateTimeKind.Utc), "early Sep 2"),
        (new DateTime(2026, 11, 1, 3, 59, 0, DateTimeKind.Utc), "Oct 31"),
        (new DateTime(2026, 11, 1, 4, 1, 0, DateTimeKind.Utc), "Nov 1 start"),
        (new DateTime(2026, 11, 2, 4, 30, 0, DateTimeKind.Utc), "Nov 1 end"),
        (new DateTime(2026, 11, 2, 5, 1, 0, DateTimeKind.Utc), "Nov 2"),
      ]
    );

    async Task<string[]> DayAsync(DateOnly day) =>
      [
        .. (
          await SearchAsync(
            f,
            me,
            new(conversation, null, day, day, "America/Toronto", null)
          )
        )
          .Hits.Select(x => x.Snippet)
          .Order(),
      ];

    Assert.Equal(["late on Sep 1"], await DayAsync(new(2026, 9, 1)));
    Assert.Equal(["early Sep 2"], await DayAsync(new(2026, 9, 2)));
    Assert.Equal(
      ["Nov 1 end", "Nov 1 start"],
      await DayAsync(new(2026, 11, 1))
    );
    Assert.Equal(
      400,
      (
        await Handler(f, me)
          .Handle(
            new(conversation, null, new(2026, 9, 1), null, "Mars/Base", null),
            default
          )
      ).StatusCode
    );
  }

  // Days no message can have are refused, not failed on: the last day
  // there is, the first, a range backwards, more than a year.
  [Theory]
  [InlineData("2026-09-01", "9999-12-31")]
  [InlineData("0001-01-01", "2026-09-01")]
  [InlineData("2026-09-02", "2026-09-01")]
  [InlineData("2025-01-01", "2026-09-01")]
  public async Task DaysOutOfReachAreRefused(string from, string to)
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);

    var result = await Handler(f, me)
      .Handle(
        new(
          null,
          null,
          DateOnly.Parse(from),
          DateOnly.Parse(to),
          "America/Toronto",
          null
        ),
        default
      );

    Assert.Equal(400, result.StatusCode);
  }

  // In Santiago the clocks jump from midnight to one on September 6, 2026:
  // that day starts at 01:00 local (04:00 UTC), and the search answers.
  [Fact]
  public async Task ADayWhoseMidnightNeverHappensStartsAtItsFirstMoment()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var conversation = await RecordAsync(
      f,
      [
        (new DateTime(2026, 9, 6, 3, 59, 0, DateTimeKind.Utc), "Sep 5 late"),
        (new DateTime(2026, 9, 6, 4, 1, 0, DateTimeKind.Utc), "Sep 6 early"),
      ]
    );
    var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");
    Assert.True(
      zone.IsInvalidTime(
        new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Unspecified)
      )
    );

    var found = await SearchAsync(
      f,
      me,
      new(conversation, null, new(2026, 9, 6), new(2026, 9, 6), zone.Id, null)
    );

    Assert.Equal(["Sep 6 early"], found.Hits.Select(x => x.Snippet));
  }

  // Filed to the load, or its number written in the text; 14070 is not
  // 1407; the driver's current load says nothing about an old message.
  // After the load's file is filed, the load being reassigned or another
  // load being given to the driver changes nothing.
  [Fact]
  public async Task ALoadIsFoundByItsFileOrItsNumberNeverByAssignment()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var conversation = await RecordAsync(
      f,
      [
        (Base, "BOL attached"),
        (Base.AddMinutes(1), "Load 1407 is loaded, heading out"),
        (Base.AddMinutes(2), "Order 14070 confirmed"),
        (Base.AddMinutes(3), "Just checking in"),
      ]
    );
    var messages = await f
      .Db.ConversationMessages.AsNoTracking()
      .OrderBy(x => x.SentAt)
      .ToListAsync();
    var load = await FileAsync(f, messages[0].Id, 1407);
    await LoadAsync(f, 1500);

    var found = await SearchAsync(
      f,
      me,
      new(conversation, null, null, null, null, 1407)
    );

    Assert.Equal(
      [(messages[1].Id, false, true), (messages[0].Id, true, false)],
      found.Hits.Select(x => (x.MessageId, x.FiledToLoad, x.MentionsLoad))
    );
    Assert.Empty(
      (
        await SearchAsync(
          f,
          me,
          new(conversation, null, null, null, null, 1500)
        )
      ).Hits
    );
    await f
      .Db.Dispatches.Where(x => x.Id == load)
      .ExecuteUpdateAsync(x => x.SetProperty(d => d.Status, "delivered"));
    Assert.Equal(
      2,
      (
        await SearchAsync(
          f,
          me,
          new(conversation, null, null, null, null, 1407)
        )
      )
        .Hits
        .Count
    );

    // Combined: the word and the load together.
    Assert.Equal(
      [messages[1].Id],
      (
        await SearchAsync(
          f,
          me,
          new(conversation, "heading", null, null, null, 1407)
        )
      ).Hits.Select(x => x.MessageId)
    );
  }

  [Fact]
  public async Task AnotherCompanysMessagesAreNeverFound()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    await RecordAsync(f, [(Base, "Tarp needed")]);
    var theirs = await f.Db.ConversationMessages.AsNoTracking().SingleAsync();
    await f
      .Db.ConversationMessages.IgnoreQueryFilters()
      .ExecuteUpdateAsync(x => x.SetProperty(m => m.CompanyId, Guid.NewGuid()));

    var found = await SearchAsync(
      f,
      me,
      new(null, "tarp", null, null, null, null)
    );

    Assert.Empty(found.Hits);
    Assert.NotEqual(Guid.Empty, theirs.Id);
  }

  // A result deep in history opens with up to 25 messages on each side,
  // says newer ones are above, and lets nothing be marked read.
  [Fact]
  public async Task AResultOpensAsAWindowThatMarksNothingRead()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var conversation = await RecordAsync(
      f,
      [
        .. Enumerable
          .Range(0, 300)
          .Select(i => (Base.AddMinutes(i), i == 100 ? "tarp" : $"m {i}")),
      ]
    );
    var hit = Assert.Single(
      (
        await SearchAsync(
          f,
          me,
          new(conversation, "tarp", null, null, null, null)
        )
      ).Hits
    );

    var window = (
      await Handlers(f, me)
        .Handle(
          new GetConversationQuery(conversation, null, null, hit.MessageId),
          default
        )
    ).Response!;

    Assert.Equal(51, window.Messages.Count);
    Assert.Equal(hit.MessageId, window.Messages[25].Id);
    Assert.True(window.Newer);
    Assert.True(window.Older);
    Assert.Equal(0, window.ReadThrough);
  }

  private static async Task<MessageSearchView> SearchAsync(
    DispatchSyncFixture f,
    Guid user,
    SearchMessagesQuery query
  )
  {
    var result = await Handler(f, user).Handle(query, default);
    Assert.True(result.Success, $"status {result.StatusCode}");
    return result.Response!;
  }

  private static MessageSearchHandler Handler(DispatchSyncFixture f, Guid user)
  {
    f.Db.ChangeTracker.Clear();
    var identity = f
      .Db.Users.AsNoTracking()
      .Where(x => x.Id == user)
      .Select(x => x.IdentityUserId)
      .Single();
    return new(f.Db, new Caller(identity), new TestDriverScope());
  }

  private static async Task<Guid> RecordAsync(
    DispatchSyncFixture f,
    (DateTime At, string Text)[] messages
  )
  {
    f.Db.ChangeTracker.Clear();
    var i = 0;
    await new InboxRecorder(f.Db, TimeProvider.System).RecordAsync(
      DriverMessageChannels.WhatsApp,
      "123456",
      [
        .. messages.Select(x => new DriverMessageInboundEvent(Phone, x.At)
        {
          ProviderMessageId = $"wamid.search.{i++}",
          Text = x.Text,
        }),
      ],
      default
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return await f.Db.Conversations.Select(x => x.Id).SingleAsync();
  }

  private static async Task<Guid> LoadAsync(DispatchSyncFixture f, int number)
  {
    var load = new Load
    {
      Id = Guid.NewGuid(),
      LoadNumber = number,
      Status = "in_transit",
    };
    f.Db.Dispatches.Add(load);
    await f.Db.SaveChangesAsync();
    return load.Id;
  }

  private static async Task<Guid> FileAsync(
    DispatchSyncFixture f,
    Guid message,
    int number
  )
  {
    var load = await LoadAsync(f, number);
    var attachment = new MessageAttachment
    {
      Id = Guid.NewGuid(),
      MessageId = message,
      OriginalName = "bol.pdf",
      DeclaredType = "application/pdf",
      Caption = "",
      State = MessageAttachmentStates.Stored,
      CreatedAt = Base,
    };
    f.Db.MessageAttachments.Add(attachment);
    f.Db.DispatchDocuments.Add(
      new DispatchDocument
      {
        Id = Guid.NewGuid(),
        DispatchId = load,
        SourceAttachmentId = attachment.Id,
        Content = [1],
        ContentType = "application/pdf",
        FileName = "bol.pdf",
        Length = 1,
      }
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return load;
  }
}
