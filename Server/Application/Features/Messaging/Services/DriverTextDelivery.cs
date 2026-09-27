using Application.Features.Messaging.Interfaces;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Rules.Messaging;
using Microsoft.Extensions.Logging;

namespace Application.Features.Messaging.Services;

// Messaging's delivery of one text for another module. It is an external
// operation, not a database change: the attempt row is committed before the
// provider is called, so an attempt that dies half way is found as unknown
// rather than tried again unseen, and the business number it went from is
// recorded so only that number's statuses can move it.
public sealed class DriverTextDelivery(
  IAppDbContext db,
  IDriverMessaging messaging,
  ICurrentCompany company,
  TimeProvider time,
  EarlyDeliveryStatuses early,
  ILogger<DriverTextDelivery> logger
) : IDriverTextDelivery
{
  public string Channel => messaging.Channel;

  public async Task<DriverTextRecipient> RecipientAsync(
    Guid? driverId,
    CancellationToken ct
  )
  {
    var driver = driverId is { } id
      ? await DriverRecipients
        .Candidates(db.Drivers.AsNoTracking())
        .SingleOrDefaultAsync(x => x.Id == id, ct)
      : null;
    var recipient = driver?.Recipient;
    var readiness = await ReadinessAsync(recipient?.Number, ct);
    var availability =
      !readiness.Configured ? DriverTextAvailability.NotConfigured
      : driver is null ? DriverTextAvailability.NoDriver
      : recipient?.Source == DriverWhatsAppSources.InvalidWhatsApp
        ? DriverTextAvailability.InvalidRecipient
      : recipient?.Number is null ? DriverTextAvailability.NoRecipient
      : readiness.WindowEnds is null ? DriverTextAvailability.Unavailable
      : DriverTextAvailability.Ready;
    return new(
      driver?.Id,
      driver?.Name,
      recipient?.Number,
      availability,
      readiness.WindowEnds
    );
  }

  // The same window a conversation reply is held to: the driver's latest
  // message to the company's current business number.
  public async Task<DriverTextReadiness> ReadinessAsync(
    string? phone,
    CancellationToken ct
  )
  {
    if (await messaging.BusinessNumberAsync(ct) is not { } number)
      return new(false, null);
    return new(
      true,
      phone is null ? null : await WindowEndsAsync(number, phone, ct)
    );
  }

  // Only a conversation under this business number opens its window. The
  // legacy window rows name no number, so they open nothing: a window is
  // never guessed.
  private async Task<DateTime?> WindowEndsAsync(
    string number,
    string phone,
    CancellationToken ct
  )
  {
    var last = await db
      .Conversations.AsNoTracking()
      .Where(x =>
        x.Channel == messaging.Channel
        && x.BusinessNumberId == number
        && x.Participant == phone
      )
      .Select(x => x.LastInboundAt)
      .SingleOrDefaultAsync(ct);
    return DriverMessageProgress.WindowOpen(last, time.GetUtcNow().UtcDateTime)
      ? last!.Value + DriverMessageProgress.SessionWindow
      : null;
  }

  public async Task<DriverTextOutcome> SendAsync(
    DriverMessage request,
    bool sendAgain,
    Func<CancellationToken, Task<bool>> stillWanted,
    CancellationToken ct
  )
  {
    if (request.Text.Length > 4096)
      return new(DriverTextResult.TooLong, null);
    var now = time.GetUtcNow().UtcDateTime;
    var latest = await db
      .DriverMessages.Where(x => x.IdempotencyKey == request.IdempotencyKey)
      .OrderByDescending(x => x.Attempt)
      .FirstOrDefaultAsync(ct);
    if (latest is not null)
    {
      if (DriverMessageProgress.Taken(latest.Status))
        return new(DriverTextResult.AlreadyTaken, latest);
      if (DriverMessageProgress.InProgress(latest.Status, latest.StatusAt, now))
        return new(DriverTextResult.InProgress, null);
      if (
        DriverMessageProgress.Uncertain(latest.Status, latest.StatusAt, now)
        && !sendAgain
      )
        return new(DriverTextResult.Uncertain, null);
    }
    if (await messaging.BusinessNumberAsync(ct) is not { } number)
      return new(DriverTextResult.NotConfigured, null);
    if (request.Id == Guid.Empty)
      request.Id = Guid.NewGuid();
    request.CompanyId =
      company.Id ?? throw new InvalidOperationException("A company is needed.");
    request.Channel = messaging.Channel;
    request.BusinessNumberId = number;
    request.Attempt = (latest?.Attempt ?? 0) + 1;
    request.ProviderMessageId = null;
    request.Status = DriverMessageStatuses.Sending;
    request.StatusAt = now;
    request.ErrorCode = null;
    request.CreatedAt = now;
    db.DriverMessages.Add(request);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (db.IsDuplicateMessageAttempt(ex))
    {
      // Another press of the same message took this attempt first.
      db.Entry(request).State = EntityState.Detached;
      return new(DriverTextResult.InProgress, null);
    }
    // From here the attempt is recorded whatever happens to the request
    // that asked for it. The requester is asked once more, and the
    // driver's window under the recorded number is read again right before
    // the call; the adapter then sends from that number or not at all.
    if (!await stillWanted(CancellationToken.None))
      return await FinishAsync(
        request,
        DriverTextResult.Withdrawn,
        DriverMessageStatuses.Withdrawn,
        null
      );
    if (
      await WindowEndsAsync(number, request.Recipient, CancellationToken.None)
      is null
    )
      return await FinishAsync(
        request,
        DriverTextResult.WindowClosed,
        DriverMessageStatuses.Withdrawn,
        null
      );
    var result = await messaging.SendTextAsync(
      number,
      request.Recipient,
      request.Text,
      CancellationToken.None
    );
    switch (result.Outcome)
    {
      case DriverMessageOutcome.NumberChanged:
        return await FinishAsync(
          request,
          DriverTextResult.NumberChanged,
          DriverMessageStatuses.Withdrawn,
          null
        );
      case DriverMessageOutcome.Accepted:
        request.ProviderMessageId = result.ProviderMessageId;
        return await AcceptedAsync(request);
      case DriverMessageOutcome.Unknown:
        logger.LogWarning(
          "WhatsApp send {DriverMessageId} has no answer, code {ErrorCode}",
          request.Id,
          result.ErrorCode
        );
        return await FinishAsync(
          request,
          DriverTextResult.Unknown,
          DriverMessageStatuses.Unknown,
          result.ErrorCode
        );
      default:
        logger.LogWarning(
          "WhatsApp refused {DriverMessageId}, code {ErrorCode}",
          request.Id,
          result.ErrorCode
        );
        return await FinishAsync(
          request,
          DriverTextResult.Rejected,
          DriverMessageStatuses.Rejected,
          result.ErrorCode
        );
    }
  }

  // The id, its acceptance and whatever the provider already reported about
  // it commit together, under the lock the webhook takes (audit F27).
  private async Task<DriverTextOutcome> AcceptedAsync(DriverMessage attempt)
  {
    await using var transaction = await db.Database.BeginTransactionAsync(
      CancellationToken.None
    );
    attempt.Status = DriverMessageStatuses.Accepted;
    attempt.StatusAt = time.GetUtcNow().UtcDateTime;
    attempt.ErrorCode = null;
    await early.ApplyAsync(attempt, CancellationToken.None);
    await db.SaveChangesAsync(CancellationToken.None);
    await transaction.CommitAsync(CancellationToken.None);
    return new(DriverTextResult.Accepted, attempt);
  }

  private async Task<DriverTextOutcome> FinishAsync(
    DriverMessage attempt,
    DriverTextResult result,
    string status,
    int? errorCode
  )
  {
    attempt.Status = status;
    attempt.StatusAt = time.GetUtcNow().UtcDateTime;
    attempt.ErrorCode = errorCode;
    await db.SaveChangesAsync(CancellationToken.None);
    return new(result, attempt);
  }
}
