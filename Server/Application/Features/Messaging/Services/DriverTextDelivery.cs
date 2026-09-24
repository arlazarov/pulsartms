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
  ILogger<DriverTextDelivery> logger
) : IDriverTextDelivery
{
  public string Channel => messaging.Channel;

  // The same window a conversation reply is held to: the driver's latest
  // message to the company's current business number.
  public async Task<DriverTextReadiness> ReadinessAsync(
    string? phone,
    CancellationToken ct
  )
  {
    if (await messaging.BusinessNumberAsync(ct) is not { } number)
      return new(false, null);
    if (phone is null)
      return new(true, null);
    var last = await db
      .Conversations.AsNoTracking()
      .Where(x =>
        x.Channel == messaging.Channel
        && x.BusinessNumberId == number
        && x.Participant == phone
      )
      .Select(x => x.LastInboundAt)
      .SingleOrDefaultAsync(ct);
    return new(
      true,
      DriverMessageProgress.WindowOpen(last, time.GetUtcNow().UtcDateTime)
        ? last!.Value + DriverMessageProgress.SessionWindow
        : null
    );
  }

  public async Task<DriverTextOutcome> SendAsync(
    DriverMessage request,
    bool sendAgain,
    Func<CancellationToken, Task<bool>> stillWanted,
    CancellationToken ct
  )
  {
    var now = time.GetUtcNow().UtcDateTime;
    var latest = await db
      .DriverMessages.Where(x => x.IdempotencyKey == request.IdempotencyKey)
      .OrderByDescending(x => x.Attempt)
      .FirstOrDefaultAsync(ct);
    if (latest is not null)
    {
      if (DriverMessageProgress.Taken(latest.Status))
        return new(DriverTextResult.AlreadyTaken, null);
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
    catch (DbUpdateException)
    {
      // Another press of the same message took this attempt first.
      db.Entry(request).State = EntityState.Detached;
      return new(DriverTextResult.InProgress, null);
    }
    // From here the attempt is recorded whatever happens to the request
    // that asked for it.
    if (!await stillWanted(CancellationToken.None))
      return await FinishAsync(
        request,
        DriverTextResult.Withdrawn,
        DriverMessageStatuses.Withdrawn,
        null
      );
    var result = await messaging.SendTextAsync(
      request.Recipient,
      request.Text,
      CancellationToken.None
    );
    switch (result.Outcome)
    {
      case DriverMessageOutcome.Accepted:
        request.ProviderMessageId = result.ProviderMessageId;
        return await FinishAsync(
          request,
          DriverTextResult.Accepted,
          DriverMessageStatuses.Accepted,
          null
        );
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
