using System.Data.Common;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Fleet;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Messaging;

// One message to several drivers: each eligible driver gets their own
// message in their own conversation, under the rules a single reply has -
// a text only inside the reply window, a template to anyone with a number,
// the company's own name filled in by the server. A preview writes nothing;
// the retry key makes one broadcast of two requests; cancelling withdraws
// only what has not started to go, and says so for the rest.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class BroadcastTests
{
  [Fact]
  public async Task APreviewSaysWhoCanBeSentWhatAndWritesNothing()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var d = await DriversAsync(f);
    var conversations = await f.Db.Conversations.CountAsync();

    var text = await PreviewAsync(
      f,
      new("all", null, "Stop at the yard", null, null, null)
    );
    var template = await PreviewAsync(f, Contact("all"));

    Assert.Equal(
      [
        (d.Ann, true, null),
        (d.Bo, false, "window"),
        (d.Cy, false, "not valid"),
        (d.Di, false, "same number"),
      ],
      text.Recipients.Select(x => (x.DriverId, x.Eligible, Short(x.Reason)))
    );
    Assert.Equal(
      [(d.Ann, true), (d.Bo, true), (d.Cy, false), (d.Di, false)],
      template.Recipients.Select(x => (x.DriverId, x.Eligible))
    );
    Assert.Equal(
      "Dispatch at AMF Carrier would like to speak with you. Please reply "
        + "when it’s safe.",
      template.Body
    );
    Assert.Equal(conversations, await f.Db.Conversations.CountAsync());
    Assert.Empty(await f.Db.MessageBroadcasts.ToListAsync());
  }

  [Fact]
  public async Task EachDriverGetsTheirOwnMessageOnceForItsKey()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var d = await DriversAsync(f);
    var key = Guid.NewGuid();

    var first = await CreateAsync(f, key, Contact("all"));
    var again = await CreateAsync(f, key, Contact("all"));

    Assert.Equal(first.Id, again.Id);
    var sent = first.Recipients.Where(x => x.MessageId is not null).ToList();
    Assert.Equal([d.Ann, d.Bo], sent.Select(x => x.DriverId));
    Assert.Equal(2, sent.Select(x => x.ConversationId).Distinct().Count());
    Assert.Equal(
      ["skipped", "skipped"],
      first.Recipients.Where(x => x.MessageId is null).Select(x => x.Status)
    );
    Assert.Equal(
      2,
      await f
        .Db.ConversationMessages.Where(x =>
          x.Direction == MessageDirections.Outbound
        )
        .CountAsync()
    );

    await f.Worker.RunOnceAsync(default);
    Assert.Equal(
      ["+15550000001", "+15550000002"],
      f.Messaging.Templates.Select(x => x.To).Order()
    );
    Assert.All(
      f.Messaging.Templates,
      x => Assert.Equal("AMF Carrier", Assert.Single(x.Parameters))
    );
  }

  [Fact]
  public async Task TheScopeIsTheGroupOrTheDriversChosen()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var d = await DriversAsync(f);

    var group = await PreviewAsync(
      f,
      Contact("group"),
      new DriverScope(Guid.NewGuid(), "West", [d.Bo], [])
    );
    var chosen = await PreviewAsync(
      f,
      Contact("selected") with
      {
        DriverIds = [d.Ann, d.Other],
      }
    );

    Assert.Equal([d.Bo], group.Recipients.Select(x => x.DriverId));
    Assert.Equal([d.Ann], chosen.Recipients.Select(x => x.DriverId));
    Assert.Equal(
      400,
      (
        await Handlers(f)
          .Handle(
            new PreviewBroadcastQuery(
              Contact("selected") with
              {
                DriverIds =
                [
                  .. Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()),
                ],
              }
            ),
            default
          )
      ).StatusCode
    );
  }

  // Cancelled before the worker takes them: withdrawn, never sent.
  [Fact]
  public async Task CancellingBeforeTheyGoWithdrawsThem()
  {
    await using var f = await ReplyFixture.CreateAsync();
    await DriversAsync(f);
    var broadcast = await CreateAsync(f, Guid.NewGuid(), Contact("all"));

    var cancelled = (
      await Handlers(f)
        .Handle(new CancelBroadcastCommand(broadcast.Id), default)
    ).Response!;
    await f.Worker.RunOnceAsync(default);

    Assert.Empty(f.Messaging.Templates);
    Assert.NotNull(cancelled.CancelledAt);
    Assert.All(
      cancelled.Recipients.Where(x => x.MessageId is not null),
      x => Assert.Equal(DriverMessageStatuses.Withdrawn, x.Status)
    );
  }

  // The worker has taken the message, but not yet recorded that it is
  // sending, when the dispatcher cancels: the withdrawal moves its fence,
  // the worker's next step fails, and the provider is never called.
  [Fact]
  public async Task CancellingAfterTheWorkerTookOneStillStopsIt()
  {
    await using var f = await ReplyFixture.CreateAsync();
    await DriversAsync(f);
    var broadcast = await CreateAsync(
      f,
      Guid.NewGuid(),
      Contact("selected") with
      {
        DriverIds = [(await DriversAsync(f, only: true)).Ann],
      }
    );
    var hook = new BeforeSending(
      async () =>
        await Handlers(f)
          .Handle(new CancelBroadcastCommand(broadcast.Id), default)
    );
    f.Interceptors.Add(hook);

    await f.Worker.RunOnceAsync(default);

    Assert.True(hook.Ran);
    Assert.Empty(f.Messaging.Templates);
    var view = (
      await Handlers(f).Handle(new GetBroadcastQuery(broadcast.Id), default)
    ).Response!;
    Assert.Equal(
      DriverMessageStatuses.Withdrawn,
      view.Recipients.Single(x => x.MessageId is not null).Status
    );
  }

  // Cancelled while the provider already has it: it is not undone, and the
  // broadcast says it went.
  [Fact]
  public async Task CancellingDuringTheSendDoesNotClaimToStopIt()
  {
    await using var f = await ReplyFixture.CreateAsync();
    await DriversAsync(f);
    var broadcast = await CreateAsync(
      f,
      Guid.NewGuid(),
      Contact("selected") with
      {
        DriverIds = [(await DriversAsync(f, only: true)).Ann],
      }
    );
    var cancelled = false;
    f.Messaging.BeforeSend = () =>
    {
      if (cancelled)
        return;
      cancelled = true;
      using var db = f.Context();
      Handlers(f, db)
        .Handle(new CancelBroadcastCommand(broadcast.Id), default)
        .GetAwaiter()
        .GetResult();
    };

    await f.Worker.RunOnceAsync(default);

    Assert.True(cancelled);
    Assert.Single(f.Messaging.Templates);
    var view = (
      await Handlers(f).Handle(new GetBroadcastQuery(broadcast.Id), default)
    ).Response!;
    Assert.NotNull(view.CancelledAt);
    Assert.NotEqual(
      DriverMessageStatuses.Withdrawn,
      view.Recipients.Single(x => x.MessageId is not null).Status
    );
  }

  private static BroadcastRequest Contact(string scope) =>
    new(scope, null, null, "contact_request", "en_US", null);

  private static string? Short(string? reason) =>
    reason switch
    {
      null => null,
      _ when reason.Contains("24 hours") => "window",
      _ when reason.Contains("not valid") => "not valid",
      _ when reason.Contains("same number") => "same number",
      _ => reason,
    };

  private static async Task<BroadcastPreview> PreviewAsync(
    ReplyFixture f,
    BroadcastRequest request,
    DriverScope? scope = null
  )
  {
    var result = await Handlers(f, scope: scope)
      .Handle(new PreviewBroadcastQuery(request), default);
    Assert.True(result.Success, $"status {result.StatusCode}");
    return result.Response!;
  }

  private static async Task<BroadcastView> CreateAsync(
    ReplyFixture f,
    Guid key,
    BroadcastRequest request
  )
  {
    var result = await Handlers(f)
      .Handle(new CreateBroadcastCommand(key, request), default);
    Assert.True(result.Success, $"status {result.StatusCode}");
    return result.Response!;
  }

  private static BroadcastHandlers Handlers(
    ReplyFixture f,
    Infrastructure.Persistence.AppDbContext? db = null,
    DriverScope? scope = null
  )
  {
    var context = db ?? f.Db;
    context.ChangeTracker.Clear();
    return new(
      context,
      new ReplyFixture.Caller("me"),
      new TestCompany(),
      new TestDriverScope(scope),
      f.Messaging,
      new ApprovedTemplates(context, f.Messaging),
      new ConversationOpener(
        context,
        f.Messaging,
        new TestCompany(),
        f.Events,
        f.Clock
      ),
      new ReplyQueue(context, new TestCompany(), f.Events, f.Signal, f.Clock),
      f.Events,
      f.Signal,
      f.Clock
    );
  }

  private sealed record Drivers(
    Guid Ann,
    Guid Bo,
    Guid Cy,
    Guid Di,
    Guid Other
  );

  // Ann has written within the day; Bo has only a phone and has never
  // written; Cy's WhatsApp number is not valid; Di shares Ann's number; a
  // driver of another company and an inactive one are never asked. The
  // contact request is recorded as approved and the company named.
  private static async Task<Drivers> DriversAsync(
    ReplyFixture f,
    bool only = false
  )
  {
    if (only)
    {
      var existing = await f
        .Db.Drivers.AsNoTracking()
        .IgnoreQueryFilters()
        .ToDictionaryAsync(x => x.Name, x => x.Id);
      return new(
        existing["Ann"],
        existing["Bo"],
        existing["Cy"],
        existing["Di"],
        existing["Other"]
      );
    }
    f.Db.Companies.Add(
      new Company
      {
        Id = Company.Amf,
        Key = "amfcarrier",
        Name = "AMF Carrier",
      }
    );
    Driver Add(string name, string? whatsApp, string? phone, bool active = true)
    {
      var driver = new Driver
      {
        Id = Guid.NewGuid(),
        ExternalId = name,
        Name = name,
        IsActive = active,
        WhatsAppPhone = whatsApp,
        Phone = phone,
      };
      f.Db.Drivers.Add(driver);
      return driver;
    }
    var ann = Add("Ann", "+15550000001", null);
    var bo = Add("Bo", null, "+15550000002");
    var cy = Add("Cy", "555", null);
    var di = Add("Di", null, "+15550000001");
    Add("Inactive", "+15550000009", null, active: false);
    var other = Add("Other", "+15550000008", null);
    other.CompanyId = Guid.NewGuid();
    await f.Db.SaveChangesAsync();
    await f.ConversationAsync("+15550000001", hoursAgo: 1);
    f.Files(
      [
        new()
        {
          Name = PulsrTemplates.Contact.Name,
          Language = PulsrTemplates.Contact.Language,
          Parameters = 1,
          Text = PulsrTemplates.Contact.Body,
        },
      ]
    );
    f.Db.ChangeTracker.Clear();
    return new(ann.Id, bo.Id, cy.Id, di.Id, other.Id);
  }

  // Runs once, just before the worker records that it is sending.
  private sealed class BeforeSending(Func<Task> action) : DbCommandInterceptor
  {
    private Func<Task>? pending = action;

    public bool Ran => pending is null;

    public override async ValueTask<
      InterceptionResult<int>
    > NonQueryExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<int> result,
      CancellationToken ct = default
    )
    {
      if (
        pending is { } run
        && command.CommandText.StartsWith("UPDATE")
        && command
          .Parameters.Cast<DbParameter>()
          .Any(x => Equals(x.Value, DriverMessageStatuses.Sending))
      )
      {
        pending = null;
        await run();
      }
      return result;
    }
  }
}
