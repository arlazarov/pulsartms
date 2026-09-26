using Application.Features.Routing.Commands;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Models.Routing;
using Domain.Rules.Messaging;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

// Sends this shift's fuel through Messaging: the plan and driver are decided
// here, and Messaging (IDriverTextDelivery) records and makes the attempt.
// It is an external operation, not a database change: the database and
// the provider cannot be committed together, and an answer that never came
// cannot be turned into exactly once. So:
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
  IDriverTextDelivery delivery,
  FuelIssueRecords records
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
    var recipient = current.Preview.Recipient!.Address!;
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
    var key = FuelIssueChannel.Key(
      current.Saved,
      visits.Select(x => x.Stop),
      recipient
    );
    var saved = current.Saved;
    var message = new DriverMessage
    {
      DriverId = current.Preview.Recipient.DriverId!.Value,
      TruckId = saved.TruckId,
      DispatchId = saved.RootDispatchId,
      ExecutionLegId = saved.RootExecutionLegId,
      AssignmentRevision = saved.AssignmentRevision,
      PlanCalculatedAt = saved.CalculatedAt,
      Recipient = recipient,
      Text = text,
      VisitKeys = string.Join(
        ',',
        visits.Select(x => FuelVisitIdentity.Key(x.Stop))
      ),
      IdempotencyKey = key,
      CreatedBy = actor,
    };
    var outcome = await delivery.SendAsync(
      message,
      request.SendAgain,
      async again =>
        await read(again) is { } after && Refusal(after, plan) is null,
      ct
    );
    switch (outcome.Result)
    {
      case DriverTextResult.Accepted:
        await records.RecordAsync(
          saved,
          visits,
          outcome.Attempt!.Channel,
          actor,
          CancellationToken.None,
          outcome.Attempt!.Id
        );
        return Outcome.Done;
      case DriverTextResult.TooLong:
        return new(400, "The message exceeds the delivery size limit.");
      case DriverTextResult.InProgress:
        return new(409, "This plan is being sent now.");
      case DriverTextResult.Uncertain:
        return new(
          409,
          "The delivery service did not answer, so the last attempt may have been "
            + "delivered. Check with the driver, then send again only if "
            + "it did not arrive."
        );
      case DriverTextResult.NotConfigured:
        return new(
          409,
          "Driver messaging is not set up. An administrator adds it in Settings."
        );
      case DriverTextResult.Withdrawn:
        return new(
          409,
          "The fuel plan changed while it was being sent, so nothing was "
            + "sent. Open it again and send the new plan."
        );
      case DriverTextResult.WindowClosed:
        return new(
          409,
          "The channel can no longer deliver to this driver. Nothing was "
            + "sent. Refresh the recipient or pass the plan on by hand."
        );
      case DriverTextResult.NumberChanged:
        return new(
          409,
          "The company's messaging connection changed while this was being "
            + "sent, so nothing was sent. Open the plan again."
        );
      default:
        return Outcome.Done;
    }
  }

  // Whether the plan the dispatcher saw is still the one to send, to a
  // recipient the delivery owner currently marks ready.
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
        "Driver messaging is not set up. An administrator adds it in Settings."
      ),
      FuelIssueChannelStates.NoDriver => new(
        409,
        "No driver is assigned to this truck's current work."
      ),
      FuelIssueChannelStates.NoNumber => new(
        409,
        "Add a contact for the driver on the selected channel first."
      ),
      FuelIssueChannelStates.InvalidNumber => new(
        409,
        "The selected delivery address is invalid. Correct it in "
          + "the driver's contacts."
      ),
      _ => new(
        409,
        "The selected channel cannot deliver this message now. Refresh "
          + "the recipient or pass the plan on by hand."
      ),
    };
  }
}
