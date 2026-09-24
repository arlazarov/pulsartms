using Application.Features.Messaging.Interfaces;
using Application.Features.Messaging.Services;
using Application.Models;
using Domain.Models.Messaging;
using Domain.Rules.Messaging;

namespace Application.Features.Messaging.Commands;

// The provider's subscription check: the challenge is echoed only for this
// carrier's verify token.
public sealed record VerifyDriverMessagingWebhookQuery(
  string CompanyKey,
  string? Mode,
  string? VerifyToken,
  string? Challenge
) : IRequest<RequestResponse<string>>;

// A signed notification for one carrier. It is read only after its
// signature matches that carrier's app secret, and only changes for its
// own business number count. What drivers wrote and the statuses of
// dispatchers' replies are recorded here, in one transaction; the verified
// notification is returned so fuel-plan statuses can be applied by their
// owner (Routing) afterwards.
public sealed record ReceiveDriverMessagesCommand(
  string CompanyKey,
  string? Signature,
  Stream Body
) : IRequest<RequestResponse<ReceivedDriverMessages>>;

public sealed record ReceivedDriverMessages(
  Guid Company,
  DriverMessagingNotification Notification
);

public sealed class DriverMessagingWebhookHandlers(
  IAppDbContext db,
  IDriverMessaging messaging,
  ICurrentCompany companies,
  InboxRecorder inbox,
  MessagingEvents events
)
  : IRequestHandler<VerifyDriverMessagingWebhookQuery, RequestResponse<string>>,
    IRequestHandler<
      ReceiveDriverMessagesCommand,
      RequestResponse<ReceivedDriverMessages>
    >
{
  public const int MaximumBody = 262_144;

  public async Task<RequestResponse<string>> Handle(
    VerifyDriverMessagingWebhookQuery request,
    CancellationToken ct
  )
  {
    if (
      request.Mode != "subscribe"
      || request.Challenge is not { Length: > 0 and <= 128 } challenge
      || !challenge.All(char.IsAsciiLetterOrDigit)
      || await CompanyAsync(request.CompanyKey, ct) is not { } company
    )
      return RequestResponse<string>.Fail("Not accepted.", 403);
    using var serving = companies.As(company);
    return await messaging.AcceptsSubscriptionAsync(request.VerifyToken, ct)
      ? RequestResponse<string>.Ok(challenge)
      : RequestResponse<string>.Fail("Not accepted.", 403);
  }

  public async Task<RequestResponse<ReceivedDriverMessages>> Handle(
    ReceiveDriverMessagesCommand request,
    CancellationToken ct
  )
  {
    if (await CompanyAsync(request.CompanyKey, ct) is not { } company)
      return RequestResponse<ReceivedDriverMessages>.Fail("Not accepted.", 404);
    var body = await ReadAsync(request.Body, ct);
    if (body is null)
      return RequestResponse<ReceivedDriverMessages>.Fail("Not accepted.", 413);
    using var serving = companies.As(company);
    var notification = await messaging.ReadNotificationAsync(
      body,
      request.Signature,
      ct
    );
    if (notification is null)
      return RequestResponse<ReceivedDriverMessages>.Fail("Not accepted.", 401);
    var outbound = await ApplyConversationStatusesAsync(notification, ct);
    var conversations = await inbox.RecordAsync(
      messaging.Channel,
      notification.BusinessNumberId,
      notification.Inbound,
      ct
    );
    await db.SaveChangesAsync(ct);
    // After the commit: the conversations that gained or changed a message.
    foreach (
      var (conversation, revision) in await RevisionsAsync(
        conversations.Concat(outbound),
        ct
      )
    )
      events.Publish(company, new(conversation, revision));
    return RequestResponse<ReceivedDriverMessages>.Ok(
      new(company, notification)
    );
  }

  // Statuses for messages dispatchers sent from a conversation, by provider
  // id under this business number; they only move forward.
  private async Task<IReadOnlyList<Guid>> ApplyConversationStatusesAsync(
    DriverMessagingNotification notification,
    CancellationToken ct
  )
  {
    if (notification.Statuses.Count == 0)
      return [];
    var ids = notification
      .Statuses.Select(x => x.ProviderMessageId)
      .Distinct()
      .ToArray();
    var messages = await db
      .ConversationMessages.Where(x =>
        x.Channel == messaging.Channel
        && x.BusinessNumberId == notification.BusinessNumberId
        && x.ProviderMessageId != null
        && ids.Contains(x.ProviderMessageId)
      )
      .ToDictionaryAsync(x => x.ProviderMessageId!, ct);
    var changed = new HashSet<Guid>();
    foreach (var status in notification.Statuses.OrderBy(x => x.At))
      if (
        messages.GetValueOrDefault(status.ProviderMessageId) is { } message
        && DriverMessageProgress.Advances(message.Status, status.Status)
      )
      {
        message.Status = status.Status;
        message.StatusAt = status.At;
        message.ErrorCode = status.ErrorCode;
        changed.Add(message.ConversationId);
      }
    return [.. changed];
  }

  private async Task<List<(Guid, long)>> RevisionsAsync(
    IEnumerable<Guid> conversations,
    CancellationToken ct
  )
  {
    var ids = conversations.Distinct().ToArray();
    if (ids.Length == 0)
      return [];
    return (
      await db
        .Conversations.AsNoTracking()
        .Where(x => ids.Contains(x.Id))
        .Select(x => new { x.Id, x.Revision })
        .ToListAsync(ct)
    )
      .Select(x => (x.Id, x.Revision))
      .ToList();
  }

  private async Task<Guid?> CompanyAsync(string key, CancellationToken ct) =>
    string.IsNullOrWhiteSpace(key) || key.Length > 100
      ? null
      : await db
        .Companies.AsNoTracking()
        .Where(x => x.Key == key && x.IsActive)
        .Select(x => (Guid?)x.Id)
        .SingleOrDefaultAsync(ct);

  private static async Task<byte[]?> ReadAsync(
    Stream body,
    CancellationToken ct
  )
  {
    using var copy = new MemoryStream();
    var buffer = new byte[16_384];
    int read;
    while ((read = await body.ReadAsync(buffer, ct)) > 0)
    {
      if (copy.Length + read > MaximumBody)
        return null;
      copy.Write(buffer, 0, read);
    }
    return copy.ToArray();
  }
}
