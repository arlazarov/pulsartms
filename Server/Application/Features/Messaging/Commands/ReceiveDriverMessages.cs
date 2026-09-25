using Application.Diagnostics;
using Application.Features.Messaging.Interfaces;
using Application.Features.Messaging.Services;
using Application.Models;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Rules.Messaging;
using Microsoft.Extensions.Logging;

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
// own business number count. What drivers wrote, and the statuses of
// dispatchers' replies and of texts other modules asked Messaging to send,
// are recorded in one transaction, so the provider's retry of a failed
// notification finds either all of it or none. Requesters are told which
// of their texts moved after the commit.
public sealed record ReceiveDriverMessagesCommand(
  string CompanyKey,
  string? Signature,
  Stream Body
) : IRequest<RequestResponse<bool>>;

// Every notification ends in one counted outcome (stage diagnostics,
// "driver-messaging-webhook/…"): accepted, unknown company, not
// configured, signature refused, or accepted but carrying changes for
// another business number, which are dropped. A refusal or a drop is also
// logged once, by company key and reason: never a number, message id,
// text or secret. These show where delivery stops; they repair nothing.
public sealed class DriverMessagingWebhookHandlers(
  IAppDbContext db,
  IDriverMessaging messaging,
  ICurrentCompany companies,
  InboxRecorder inbox,
  MessagingEvents events,
  IEnumerable<IDriverTextObserver> observers,
  ILogger<DriverMessagingWebhookHandlers> logger
)
  : IRequestHandler<VerifyDriverMessagingWebhookQuery, RequestResponse<string>>,
    IRequestHandler<ReceiveDriverMessagesCommand, RequestResponse<bool>>
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

  public async Task<RequestResponse<bool>> Handle(
    ReceiveDriverMessagesCommand request,
    CancellationToken ct
  )
  {
    if (await CompanyAsync(request.CompanyKey, ct) is not { } company)
    {
      Refused(request.CompanyKey, "unknown-company");
      return RequestResponse<bool>.Fail("Not accepted.", 404);
    }
    var body = await ReadAsync(request.Body, ct);
    if (body is null)
      return RequestResponse<bool>.Fail("Not accepted.", 413);
    using var serving = companies.As(company);
    var notification = await messaging.ReadNotificationAsync(
      body,
      request.Signature,
      ct
    );
    if (notification is null)
    {
      Refused(
        request.CompanyKey,
        await messaging.IsConfiguredAsync(ct) ? "signature" : "not-configured"
      );
      return RequestResponse<bool>.Fail("Not accepted.", 401);
    }
    Outcome("accepted");
    PerformanceStages.Count(
      Stage,
      "inbound-messages",
      notification.Inbound.Count
    );
    PerformanceStages.Count(Stage, "statuses", notification.Statuses.Count);
    if (notification.OtherSenders > 0)
    {
      PerformanceStages.Count(
        Stage,
        "other-number-changes",
        notification.OtherSenders
      );
      logger.LogWarning(
        "Driver messaging webhook for {CompanyKey} dropped {Changes} "
          + "changes addressed to another business number",
        request.CompanyKey,
        notification.OtherSenders
      );
    }
    var outbound = await ApplyConversationStatusesAsync(notification, ct);
    var texts = await ApplyTextStatusesAsync(notification, ct);
    await RecordLegacyWindowsAsync(notification.Inbound, ct);
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
    if (texts.Count > 0)
      foreach (var observer in observers)
        observer.Changed(company, texts);
    return RequestResponse<bool>.Ok(true);
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

  // Statuses for texts sent for other modules (fuel plans), by provider id
  // under the business number each attempt went from: a delayed status
  // from another or an earlier number moves nothing. Attempts recorded
  // before the number was kept have none and are not moved at all.
  private async Task<IReadOnlyList<DriverMessage>> ApplyTextStatusesAsync(
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
    var attempts = await db
      .DriverMessages.Where(x =>
        x.Channel == messaging.Channel
        && x.BusinessNumberId == notification.BusinessNumberId
        && x.ProviderMessageId != null
        && ids.Contains(x.ProviderMessageId)
      )
      .ToDictionaryAsync(x => x.ProviderMessageId!, ct);
    var changed = new Dictionary<Guid, DriverMessage>();
    foreach (var status in notification.Statuses.OrderBy(x => x.At))
      if (
        attempts.GetValueOrDefault(status.ProviderMessageId) is { } attempt
        && DriverMessageProgress.Advances(attempt.Status, status.Status)
      )
      {
        attempt.Status = status.Status;
        attempt.StatusAt = status.At;
        attempt.ErrorCode = status.ErrorCode;
        changed[attempt.Id] = attempt;
      }
    return [.. changed.Values];
  }

  // For revisions released before 2026-09-24 only (see
  // DriverMessagingWindow): kept current so a rolling cutover leaves their
  // fuel sends working. Nothing here reads it.
  private async Task RecordLegacyWindowsAsync(
    IReadOnlyList<DriverMessageInboundEvent> inbound,
    CancellationToken ct
  )
  {
    if (inbound.Count == 0)
      return;
    var latest = inbound
      .GroupBy(x => x.Phone)
      .ToDictionary(x => x.Key, x => x.Max(y => y.At));
    var phones = latest.Keys.ToArray();
    var windows = await db
      .DriverMessagingWindows.Where(x =>
        x.Channel == messaging.Channel && phones.Contains(x.Phone)
      )
      .ToDictionaryAsync(x => x.Phone, ct);
    foreach (var (phone, at) in latest)
    {
      if (windows.GetValueOrDefault(phone) is { } window)
      {
        if (at > window.LastInboundAt)
          window.LastInboundAt = at;
        continue;
      }
      db.DriverMessagingWindows.Add(
        new DriverMessagingWindow
        {
          Id = Guid.NewGuid(),
          Channel = messaging.Channel,
          Phone = phone,
          LastInboundAt = at,
        }
      );
    }
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

  private const string Stage = "driver-messaging-webhook";

  private static void Outcome(string outcome) =>
    PerformanceStages.Count(Stage, outcome, 1);

  // The key is the path the provider called; bounded, since anyone can
  // call it.
  private void Refused(string key, string reason)
  {
    Outcome(reason);
    logger.LogWarning(
      "Driver messaging webhook refused for {CompanyKey}: {Reason}",
      key.Length <= 64 ? key : key[..64],
      reason
    );
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
