using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Fleet.Services;
using Application.Features.Messaging.Interfaces;
using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Application.Models;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Rules.Fleet;
using Domain.Rules.Messaging;

namespace Application.Features.Messaging.Commands;

// Who: every driver of the company (All), the dispatcher's chosen driver
// group (Group) or the drivers chosen (Selected). What: a text, which
// goes only to drivers whose reply window is open, or an approved
// template, which goes to anyone with a number.
public sealed record BroadcastRequest(
  string Scope,
  IReadOnlyList<Guid>? DriverIds,
  string? Text,
  string? TemplateName,
  string? TemplateLanguage,
  IReadOnlyList<string>? Parameters
);

// What a broadcast would do, written nowhere.
public sealed record PreviewBroadcastQuery(BroadcastRequest Request)
  : IRequest<RequestResponse<BroadcastPreview>>;

// Id is the dispatcher's retry key: asking again with it answers the
// broadcast already made.
public sealed record CreateBroadcastCommand(Guid Id, BroadcastRequest Request)
  : IRequest<RequestResponse<BroadcastView>>;

public sealed record GetBroadcastQuery(Guid Id)
  : IRequest<RequestResponse<BroadcastView>>;

// Takes back what has not gone yet. A message already being sent, or sent,
// is not undone and says so.
public sealed record CancelBroadcastCommand(Guid Id)
  : IRequest<RequestResponse<BroadcastView>>;

public sealed record BroadcastCandidate(
  Guid DriverId,
  string Name,
  string? Number,
  bool Eligible,
  string? Reason
);

public sealed record BroadcastPreview(
  IReadOnlyList<BroadcastCandidate> Recipients,
  int Eligible,
  string Body
);

// Status: the message's own delivery status, or "skipped" with the reason,
// or "withdrawn" for one taken back before it went.
public sealed record BroadcastRecipientView(
  Guid DriverId,
  string Name,
  Guid? ConversationId,
  Guid? MessageId,
  string Status,
  string? Reason
);

public sealed record BroadcastView(
  Guid Id,
  string Kind,
  string Body,
  string Scope,
  DateTime CreatedAt,
  DateTime? CancelledAt,
  IReadOnlyList<BroadcastRecipientView> Recipients
);

internal sealed record BroadcastRecipient(
  Guid DriverId,
  string Name,
  string? Number,
  Guid? ConversationId,
  Guid? MessageId,
  string? Skipped
);

public sealed class BroadcastHandlers(
  IAppDbContext db,
  ICurrentUser caller,
  ICurrentCompany company,
  IDriverScope scope,
  IDriverMessaging messaging,
  ApprovedTemplates templates,
  ConversationOpener opener,
  ReplyQueue queue,
  MessagingEvents events,
  OutboxSignal outbox,
  TimeProvider clock
)
  : IRequestHandler<PreviewBroadcastQuery, RequestResponse<BroadcastPreview>>,
    IRequestHandler<CreateBroadcastCommand, RequestResponse<BroadcastView>>,
    IRequestHandler<GetBroadcastQuery, RequestResponse<BroadcastView>>,
    IRequestHandler<CancelBroadcastCommand, RequestResponse<BroadcastView>>
{
  public const int MaximumRecipients = 200;

  private sealed record Content(
    string Kind,
    string Body,
    TemplatePayload? Template
  );

  private sealed record Plan(
    Content Content,
    string Scope,
    string Number,
    IReadOnlyList<Planned> Recipients
  );

  private sealed record Planned(
    Guid DriverId,
    string Name,
    string? Number,
    Guid? ConversationId,
    string? Reason
  );

  private sealed record Refusal(string Message, int Status);

  public async Task<RequestResponse<BroadcastPreview>> Handle(
    PreviewBroadcastQuery request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is null)
      return RequestResponse<BroadcastPreview>.Fail("Access denied.", 403);
    var (plan, refusal) = await PlanAsync(request.Request, ct);
    if (refusal is not null)
      return RequestResponse<BroadcastPreview>.Fail(
        refusal.Message,
        refusal.Status
      );
    return RequestResponse<BroadcastPreview>.Ok(
      new(
        [
          .. plan!.Recipients.Select(x => new BroadcastCandidate(
            x.DriverId,
            x.Name,
            x.Number,
            x.Reason is null,
            x.Reason
          )),
        ],
        plan.Recipients.Count(x => x.Reason is null),
        plan.Content.Body
      )
    );
  }

  public async Task<RequestResponse<BroadcastView>> Handle(
    CreateBroadcastCommand request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return Fail("Access denied.", 403);
    if (request.Id == Guid.Empty)
      return Fail("The request has no retry key.", 400);
    var hash = Fingerprint(request.Request);
    if (await ViewAsync(request.Id, ct) is { } earlier)
      return await HashAsync(request.Id, ct) == hash
        ? RequestResponse<BroadcastView>.Ok(earlier)
        : Fail(OtherRequest, 409);
    var (plan, refusal) = await PlanAsync(request.Request, ct);
    if (refusal is not null)
      return Fail(refusal.Message, refusal.Status);
    if (plan!.Recipients.All(x => x.Reason is not null))
      return Fail("None of these drivers can be sent this message.", 409);
    // Conversations first, each committed on its own and idempotent: a
    // retry finds the ones already opened.
    var opened = new Dictionary<Guid, Guid>();
    foreach (var recipient in plan.Recipients.Where(x => x.Reason is null))
      opened[recipient.DriverId] =
        recipient.ConversationId
        ?? await opener.OpenAsync(
          plan.Number,
          recipient.Number!,
          recipient.DriverId,
          ct
        );
    var ids = opened.Values.ToArray();
    var conversations = await db
      .Conversations.Where(x => ids.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, ct);
    var recipients = new List<BroadcastRecipient>();
    var queued = new List<Conversation>();
    foreach (var recipient in plan.Recipients)
    {
      if (recipient.Reason is { } reason)
      {
        recipients.Add(
          new(
            recipient.DriverId,
            recipient.Name,
            recipient.Number,
            recipient.ConversationId,
            null,
            reason
          )
        );
        continue;
      }
      var conversation = conversations[opened[recipient.DriverId]];
      var message = queue.Queue(
        conversation,
        plan.Content.Kind,
        plan.Content.Body,
        plan.Content.Body,
        user,
        Key(request.Id, recipient.DriverId),
        1,
        plan.Content.Template is { } template
          ? JsonSerializer.Serialize(template)
          : null
      );
      queued.Add(conversation);
      recipients.Add(
        new(
          recipient.DriverId,
          recipient.Name,
          recipient.Number,
          conversation.Id,
          message.Id,
          null
        )
      );
    }
    db.MessageBroadcasts.Add(
      new MessageBroadcast
      {
        Id = request.Id,
        CreatedBy = user,
        CreatedAt = clock.GetUtcNow().UtcDateTime,
        Kind = plan.Content.Kind,
        Body = plan.Content.Body,
        Template = plan.Content.Template is { } payload
          ? JsonSerializer.Serialize(payload)
          : null,
        Scope = plan.Scope,
        RequestHash = hash,
        RecipientsJson = JsonSerializer.Serialize(recipients),
      }
    );
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex)
    {
      // The same key committed first, or a driver wrote meanwhile: the
      // broadcast as it stands, or ask again with the same key. Any other
      // failure is not a conflict and is not reported as one.
      db.ChangeTracker.Clear();
      if (await ViewAsync(request.Id, ct) is { } first)
        return await HashAsync(request.Id, ct) == hash
          ? RequestResponse<BroadcastView>.Ok(first)
          : Fail(OtherRequest, 409);
      if (!db.IsWriteConflict(ex))
        throw;
      return Fail("The conversations changed. Send again.", 409);
    }
    if (company.Id is { } serving)
      foreach (var conversation in queued)
        events.Publish(serving, new(conversation.Id, conversation.Revision));
    outbox.Wake();
    return RequestResponse<BroadcastView>.Ok(
      (await ViewAsync(request.Id, ct))!
    );
  }

  public async Task<RequestResponse<BroadcastView>> Handle(
    GetBroadcastQuery request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is null)
      return Fail("Access denied.", 403);
    return await ViewAsync(request.Id, ct) is { } view
      ? RequestResponse<BroadcastView>.Ok(view)
      : Fail("Broadcast not found.", 404);
  }

  // Each message still waiting its turn is withdrawn: its status leaves
  // "queued" and its fence moves. A worker that took one but has not yet
  // recorded that it is sending makes that record only on a queued message
  // with its own fence, so it fails and never calls the provider. A message
  // already recorded as sending is with the provider or on its way to it:
  // it is left, it may well arrive, and the view shows its real status.
  // Nothing here can take back what the provider has.
  public async Task<RequestResponse<BroadcastView>> Handle(
    CancelBroadcastCommand request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is null)
      return Fail("Access denied.", 403);
    var broadcast = await db.MessageBroadcasts.SingleOrDefaultAsync(
      x => x.Id == request.Id,
      ct
    );
    if (broadcast is null)
      return Fail("Broadcast not found.", 404);
    var now = clock.GetUtcNow().UtcDateTime;
    var waiting = await Attempts(broadcast)
      .Where(x => x.Status == OutboundStates.Queued)
      .Select(x => new { x.Id, x.ConversationId })
      .ToListAsync(ct);
    var ids = waiting.Select(x => x.Id).ToArray();
    var chats = waiting.Select(x => x.ConversationId).Distinct().ToArray();
    Dictionary<Guid, long> revisions;
    // Like every status change, the withdrawal commits with its
    // conversations' revisions so open chats read it again.
    await using (var transaction = await db.Database.BeginTransactionAsync(ct))
    {
      await db
        .ConversationMessages.Where(x =>
          ids.Contains(x.Id) && x.Status == OutboundStates.Queued
        )
        .ExecuteUpdateAsync(
          x =>
            x.SetProperty(m => m.Status, DriverMessageStatuses.Withdrawn)
              .SetProperty(m => m.StatusAt, now)
              .SetProperty(m => m.Fence, m => m.Fence + 1),
          ct
        );
      await db
        .Conversations.Where(x => chats.Contains(x.Id))
        .ExecuteUpdateAsync(
          x => x.SetProperty(c => c.Revision, c => c.Revision + 1),
          ct
        );
      revisions = await db
        .Conversations.AsNoTracking()
        .Where(x => chats.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => x.Revision, ct);
      broadcast.CancelledAt ??= now;
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
    }
    if (company.Id is { } serving)
      foreach (var (chat, revision) in revisions)
        events.Publish(serving, new(chat, revision));
    return RequestResponse<BroadcastView>.Ok(
      (await ViewAsync(request.Id, ct))!
    );
  }

  // Who the request names and what each would be sent, or why not. Reads
  // the drivers once, their conversations on the current number once.
  private async Task<(Plan?, Refusal?)> PlanAsync(
    BroadcastRequest request,
    CancellationToken ct
  )
  {
    if (await messaging.BusinessNumberAsync(ct) is not { } number)
      return (null, new("WhatsApp is not set up for the company yet.", 409));
    var (content, problem) = await ContentAsync(request, number, ct);
    if (problem is not null)
      return (null, problem);
    var drivers = DriverRecipients.Candidates(
      db.Drivers.AsNoTracking().Where(x => x.IsActive)
    );
    string scopeName;
    switch (request.Scope)
    {
      case "all":
        scopeName = "All drivers";
        break;
      case "group":
        if (await scope.CurrentAsync(ct) is not { IsAll: false } group)
          return (null, new("Choose a driver group first.", 400));
        var members = group.Drivers;
        drivers = drivers.Where(x => members.Contains(x.Id));
        scopeName = $"Group {group.GroupName}";
        break;
      case "selected":
        if (request.DriverIds is not { Count: > 0 } chosen)
          return (null, new("Choose at least one driver.", 400));
        if (chosen.Count > MaximumRecipients)
          return (
            null,
            new($"Choose at most {MaximumRecipients} drivers.", 400)
          );
        var picked = chosen.Distinct().ToArray();
        drivers = drivers.Where(x => picked.Contains(x.Id));
        scopeName = "Chosen drivers";
        break;
      default:
        return (null, new("Choose who to send to.", 400));
    }
    var rows = await drivers
      .OrderBy(x => x.Name)
      .ThenBy(x => x.Id)
      .Take(MaximumRecipients + 1)
      .ToListAsync(ct);
    if (rows.Count > MaximumRecipients)
      return (
        null,
        new(
          $"More than {MaximumRecipients} drivers: choose a group or the "
            + "drivers to send to.",
          400
        )
      );
    var numbers = rows.Select(x => x.Recipient.Number)
      .OfType<string>()
      .Distinct()
      .ToArray();
    var channel = messaging.Channel;
    var open = await db
      .Conversations.AsNoTracking()
      .Where(x =>
        x.Channel == channel
        && x.BusinessNumberId == number
        && numbers.Contains(x.Participant)
      )
      .Select(x => new
      {
        x.Id,
        x.Participant,
        x.LastInboundAt,
      })
      .ToDictionaryAsync(x => x.Participant, ct);
    var now = clock.GetUtcNow().UtcDateTime;
    var seen = new HashSet<string>();
    var planned = new List<Planned>();
    foreach (var row in rows)
    {
      var recipient = row.Recipient;
      var conversation = recipient.Number is { } phone
        ? open.GetValueOrDefault(phone)
        : null;
      string? reason = recipient.Source switch
      {
        DriverWhatsAppSources.InvalidWhatsApp =>
          "The WhatsApp number is not valid.",
        DriverWhatsAppSources.None => "No WhatsApp number or valid phone.",
        _ => null,
      };
      if (reason is null && !seen.Add(recipient.Number!))
        reason = "Another driver here has the same number.";
      if (
        reason is null
        && content!.Kind == ConversationMessageKinds.Text
        && !DriverMessageProgress.WindowOpen(conversation?.LastInboundAt, now)
      )
        reason =
          "Has not written in the last 24 hours: only a template can go.";
      planned.Add(
        new(row.Id, row.Name, recipient.Number, conversation?.Id, reason)
      );
    }
    return (new(content!, scopeName, number, planned), null);
  }

  private async Task<(Content?, Refusal?)> ContentAsync(
    BroadcastRequest request,
    string number,
    CancellationToken ct
  )
  {
    var text = request.Text?.Trim() ?? "";
    if (request.TemplateName is not { Length: > 0 } name)
      return text.Length is 0 or > ConversationReplies.MaximumText
        ? (
          null,
          new(
            $"Write between 1 and {ConversationReplies.MaximumText} "
              + "characters, or choose a template.",
            400
          )
        )
        : (new(ConversationMessageKinds.Text, text, null), null);
    var template = await templates.FindAsync(
      number,
      name,
      request.TemplateLanguage ?? "",
      ct
    );
    if (template is null)
      return (null, new("Choose an approved template.", 400));
    var pulsr = PulsrTemplates.Matching(template);
    if (pulsr?.Unsupported is { } unsupported)
      return (null, new(unsupported, 409));
    var parameters = request.Parameters ?? [];
    if (pulsr is { CompanyNamed: true })
    {
      var company = await CompanyNameAsync(ct);
      if (string.IsNullOrWhiteSpace(company))
        return (null, new("The company has no name to send.", 409));
      parameters = [company.Trim()];
    }
    if (
      parameters.Count != template.Parameters
      || parameters.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200)
    )
      return (null, new("Fill in every field of the template.", 400));
    var names = ApprovedTemplates.Names(template.Text);
    return (
      new(
        ConversationMessageKinds.Template,
        ConversationFilesAndTemplates.Fill(template.Text, parameters, names),
        new(template.Name, template.Language, [.. parameters], names)
      ),
      null
    );
  }

  private Task<string?> CompanyNameAsync(CancellationToken ct) =>
    db
      .Companies.AsNoTracking()
      .Where(x => x.Id == company.Id)
      .Select(x => x.Name)
      .SingleOrDefaultAsync(ct);

  private async Task<BroadcastView?> ViewAsync(Guid id, CancellationToken ct)
  {
    var broadcast = await db
      .MessageBroadcasts.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == id, ct);
    if (broadcast is null)
      return null;
    var recipients = Recipients(broadcast);
    var latest = (
      await Attempts(broadcast)
        .AsNoTracking()
        .Select(x => new
        {
          x.IdempotencyKey,
          x.Attempt,
          x.Id,
          x.Status,
        })
        .ToListAsync(ct)
    ).GroupBy(x => x.IdempotencyKey!.Value).ToDictionary(x => x.Key, x => x.MaxBy(m => m.Attempt)!);
    return new(
      broadcast.Id,
      broadcast.Kind,
      broadcast.Body,
      broadcast.Scope,
      broadcast.CreatedAt,
      broadcast.CancelledAt,
      [
        .. recipients.Select(x => new BroadcastRecipientView(
          x.DriverId,
          x.Name,
          x.ConversationId,
          latest.GetValueOrDefault(Key(broadcast.Id, x.DriverId))?.Id
            ?? x.MessageId,
          x.Skipped is not null ? "skipped"
            : latest.GetValueOrDefault(Key(broadcast.Id, x.DriverId)) is { } m
              ? m.Status
            : "unknown",
          x.Skipped
        )),
      ]
    );
  }

  // Every attempt of each driver's message: a retry from the chat is a
  // new attempt under the same key, and the broadcast follows it.
  private IQueryable<ConversationMessage> Attempts(MessageBroadcast broadcast)
  {
    var sent = Recipients(broadcast).Where(x => x.MessageId is not null);
    var chats = sent.Select(x => x.ConversationId)
      .OfType<Guid>()
      .Distinct()
      .ToArray();
    var keys = sent.Select(x => (Guid?)Key(broadcast.Id, x.DriverId)).ToArray();
    return db.ConversationMessages.Where(x =>
      chats.Contains(x.ConversationId) && keys.Contains(x.IdempotencyKey)
    );
  }

  private Task<string?> HashAsync(Guid id, CancellationToken ct) =>
    db
      .MessageBroadcasts.AsNoTracking()
      .Where(x => x.Id == id)
      .Select(x => x.RequestHash)
      .SingleOrDefaultAsync(ct);

  private const string OtherRequest =
    "This retry key was used for another broadcast.";

  // The request as asked, independent of order and spacing.
  internal static string Fingerprint(BroadcastRequest request) =>
    Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(
          new
          {
            Scope = request.Scope?.Trim().ToLowerInvariant(),
            Drivers = request.DriverIds?.Distinct().Order().ToArray(),
            Text = request.Text?.Trim(),
            request.TemplateName,
            request.TemplateLanguage,
            request.Parameters,
          }
        )
      )
    );

  private static List<BroadcastRecipient> Recipients(
    MessageBroadcast broadcast
  ) =>
    JsonSerializer.Deserialize<List<BroadcastRecipient>>(
      broadcast.RecipientsJson
    ) ?? [];

  // The retry key of one driver's message in a broadcast: the same for the
  // same broadcast and driver, so asking twice queues it once.
  internal static Guid Key(Guid broadcast, Guid driver)
  {
    var hash = SHA256.HashData(
      Encoding.UTF8.GetBytes($"broadcast:{broadcast:N}:{driver:N}")
    );
    return new Guid(hash.AsSpan(0, 16));
  }

  private static RequestResponse<BroadcastView> Fail(
    string message,
    int status
  ) => RequestResponse<BroadcastView>.Fail(message, status);
}
