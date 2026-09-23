using Application.Features.Routing.Commands;
using Application.Features.Routing.Interfaces;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Models.Routing;
using Domain.Rules.Messaging;
using Domain.Rules.Routing;
using Microsoft.Extensions.Logging;

namespace Application.Features.Routing.Services.FuelPlanning;

// Sends this shift's fuel over WhatsApp. It is an external operation, not
// a database change: the database and WhatsApp cannot be committed
// together, and an answer that never came cannot be turned into exactly
// once. So:
//
// - What goes is fixed first: the words, the recipient and the visits of
//   the plan version the dispatcher saw, in an attempt row committed
//   before the provider is called.
// - The version is checked when the request is accepted and again right
//   before the call. A plan that moved in between withdraws the attempt.
// - The same instruction to the same recipient is one message: a second
//   press returns the first attempt. An attempt without an answer is
//   unknown and is only repeated when a dispatcher says so.
// - A plan that moves during the call does not undo the send: the visits
//   are recorded as the snapshot said them, and the plan that now says
//   something else reads as changed since sent.
public sealed class FuelIssueSender(
  IAppDbContext db,
  IDriverMessaging messaging,
  FuelIssueRecords records,
  ICurrentCompany company,
  TimeProvider time,
  ILogger<FuelIssueSender> logger
)
{
  public sealed record Outcome(int Status, string? Error)
  {
    public static readonly Outcome Done = new(200, null);
  }

  public async Task<Outcome> SendAsync(
    FuelIssueSendRequest request,
    Func<CancellationToken, Task<FuelIssuePreviews.Current?>> read,
    string? actor,
    CancellationToken ct
  )
  {
    var plan = request.Plan;
    var current = await read(ct);
    if (current is null)
      return new(404, "There is no fuel plan to hand over for this load.");
    if (Refusal(current, plan) is { } refused)
      return refused;
    var recipient = current.Preview.Recipient!.WhatsAppPhone!;
    var byKey = current.Visits.ToDictionary(x => FuelVisitIdentity.Key(x.Stop));
    var visits = plan.VisitKeys.Distinct().Select(key => byKey[key]).ToList();
    if (
      !request.SendAgain
      && visits.All(x =>
        x.Stop.Sent is { Changed: false } sent
        && sent.Delivery != DriverMessageStatuses.Failed
      )
    )
      return new(
        409,
        "These stops were already sent and the plan still says the same. "
          + "Nothing was sent again."
      );
    var text = FuelIssueMessage.Compose(visits.Select(x => x.Text).ToList());
    if (text.Length > 4096)
      return new(400, "The message is too long for WhatsApp.");
    var key = FuelIssueChannel.Key(
      current.Saved,
      visits.Select(x => x.Stop),
      recipient
    );
    var now = time.GetUtcNow().UtcDateTime;
    var latest = await db
      .DriverMessages.Where(x => x.IdempotencyKey == key)
      .OrderByDescending(x => x.Attempt)
      .FirstOrDefaultAsync(ct);
    if (latest is not null)
    {
      if (DriverMessageProgress.Taken(latest.Status))
        return Outcome.Done;
      if (DriverMessageProgress.InProgress(latest.Status, latest.StatusAt, now))
        return new(409, "This plan is being sent now.");
      if (
        DriverMessageProgress.Uncertain(latest.Status, latest.StatusAt, now)
        && !request.SendAgain
      )
        return new(
          409,
          "WhatsApp did not answer the last attempt, so it may have been "
            + "delivered. Check with the driver, then send again only if "
            + "it did not arrive."
        );
    }
    var saved = current.Saved;
    var message = new DriverMessage
    {
      Id = Guid.NewGuid(),
      CompanyId =
        company.Id
        ?? throw new InvalidOperationException("A company is needed."),
      DriverId = current.Preview.Recipient.DriverId!.Value,
      TruckId = saved.TruckId,
      DispatchId = saved.RootDispatchId,
      ExecutionLegId = saved.RootExecutionLegId,
      AssignmentRevision = saved.AssignmentRevision,
      PlanCalculatedAt = saved.CalculatedAt,
      Channel = messaging.Channel,
      Recipient = recipient,
      Text = text,
      VisitKeys = string.Join(
        ',',
        visits.Select(x => FuelVisitIdentity.Key(x.Stop))
      ),
      IdempotencyKey = key,
      Attempt = (latest?.Attempt ?? 0) + 1,
      Status = DriverMessageStatuses.Sending,
      StatusAt = now,
      CreatedAt = now,
      CreatedBy = actor,
    };
    db.DriverMessages.Add(message);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      // Another press of the same message took this attempt first.
      db.Entry(message).State = EntityState.Detached;
      return new(409, "This plan is being sent now.");
    }
    // From here the attempt is recorded whatever happens to the request
    // that asked for it.
    var after = await read(CancellationToken.None);
    if (after is null || Refusal(after, plan) is not null)
      return await FinishAsync(
        message,
        DriverMessageStatuses.Withdrawn,
        null,
        new(
          409,
          "The fuel plan changed while it was being sent, so nothing was "
            + "sent. Open it again and send the new plan."
        )
      );
    var result = await messaging.SendTextAsync(
      recipient,
      text,
      CancellationToken.None
    );
    switch (result.Outcome)
    {
      case DriverMessageOutcome.Accepted:
        message.ProviderMessageId = result.ProviderMessageId;
        await FinishAsync(message, DriverMessageStatuses.Accepted, null, null);
        await records.RecordAsync(
          saved,
          visits,
          FuelSendChannels.WhatsApp,
          actor,
          CancellationToken.None,
          message.Id
        );
        return Outcome.Done;
      case DriverMessageOutcome.Unknown:
        logger.LogWarning(
          "WhatsApp send {DriverMessageId} has no answer, code {ErrorCode}",
          message.Id,
          result.ErrorCode
        );
        return await FinishAsync(
          message,
          DriverMessageStatuses.Unknown,
          result.ErrorCode,
          Outcome.Done
        );
      default:
        logger.LogWarning(
          "WhatsApp refused {DriverMessageId}, code {ErrorCode}",
          message.Id,
          result.ErrorCode
        );
        return await FinishAsync(
          message,
          DriverMessageStatuses.Rejected,
          result.ErrorCode,
          Outcome.Done
        );
    }
  }

  // Whether the plan the dispatcher saw is still the one to send, to a
  // recipient WhatsApp will deliver a free-form message to now.
  private static Outcome? Refusal(
    FuelIssuePreviews.Current current,
    FuelIssueSentRequest plan
  )
  {
    var keys = current.Visits.Select(x => FuelVisitIdentity.Key(x.Stop));
    if (
      ConfirmFuelIssueSentHandler.Moved(current.Saved, plan, keys.ToHashSet())
    )
      return new(
        409,
        "The fuel plan changed since it was opened. Open it again and send "
          + "the new plan."
      );
    return current.Preview.Recipient?.State switch
    {
      FuelIssueChannelStates.Ready => null,
      FuelIssueChannelStates.NotConfigured => new(
        409,
        "WhatsApp is not set up. An administrator adds it in Settings."
      ),
      FuelIssueChannelStates.NoDriver => new(
        409,
        "No driver is assigned to this truck's current work."
      ),
      FuelIssueChannelStates.NoNumber => new(
        409,
        "Add the driver's WhatsApp number first."
      ),
      _ => new(
        409,
        "The driver has not written to the WhatsApp number in the last 24 "
          + "hours, so WhatsApp would not deliver this message. Ask the "
          + "driver to send any message, or copy the plan and send it by "
          + "hand."
      ),
    };
  }

  private async Task<Outcome> FinishAsync(
    DriverMessage message,
    string status,
    int? errorCode,
    Outcome? outcome
  )
  {
    message.Status = status;
    message.StatusAt = time.GetUtcNow().UtcDateTime;
    message.ErrorCode = errorCode;
    await db.SaveChangesAsync(CancellationToken.None);
    return outcome ?? Outcome.Done;
  }
}
