using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Messaging;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Commands;

// Handing this shift's fuel to a driver.
//
// The preview is what would be handed over now: the current-shift stops in
// words from the itinerary, the plan version they came from, who would get
// them over WhatsApp and what became of the last attempt. Opening it or
// copying it records nothing. Words passed on by hand are confirmed
// separately - against the version previewed, so a plan that moved in
// between is not recorded as sent.
public sealed record GetFuelIssuePreviewQuery(
  Guid DispatchId,
  Guid? ExecutionLegId = null
) : IRequest<RequestResponse<FuelIssuePreview>>, IPlanningRequest, IAboutWork
{
  public Guid? Load => DispatchId;
}

public sealed record ConfirmFuelIssueSentCommand(
  Guid DispatchId,
  FuelIssueSentRequest Sent
)
  : IRequest<RequestResponse<FuelIssuePreview>>,
    IPlanningRequest,
    IChecked,
    IAboutWork
{
  public Guid? Load => DispatchId;

  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
    if (Sent is null)
      yield return "The hand-over to confirm is missing.";
    else
    {
      if (
        Sent.ExpectedCalculatedAt == default
        || Sent.ExpectedCalculatedAt.Kind != DateTimeKind.Utc
      )
        yield return "Open the plan to send before confirming it.";
      if (
        Sent.VisitKeys is not { Count: > 0 and <= 40 } keys
        || keys.Any(string.IsNullOrWhiteSpace)
      )
        yield return "Choose the fuel stops that were sent.";
    }
  }
}

public sealed class FuelIssuePreviews(
  PlanningReadService reads,
  TruckFuelPlans plans,
  FuelIssueChannel channel
)
{
  public sealed record Current(
    TruckFuelPlanSnapshot Saved,
    FuelIssuePreview Preview,
    IReadOnlyList<(FuelPlanStop Stop, string Text)> Visits
  );

  public async Task<Current?> ReadAsync(
    Guid dispatchId,
    Guid? executionLegId,
    CancellationToken ct
  )
  {
    var result = await reads.ForDispatchAsync(
      dispatchId,
      ct,
      executionLegId: executionLegId
    );
    if (
      result.State?.Plan is not { FuelPlan: { } fuel } plan
      || await plans.ReadAsync(plan.TruckId, ct) is not { } saved
    )
      return null;
    var visits = fuel
      .Stops.Where(x => x.IssueHorizon == FuelIssueHorizons.Current)
      .Select(x =>
        (
          Stop: x,
          Text: FuelIssueMessage.Line(
            x,
            saved.Stops,
            plan.DispatchId,
            plan.Tracking.NextStopId
          )
        )
      )
      .ToList();
    return new(
      saved,
      new(
        plan.TruckId,
        saved.CalculatedAt,
        saved.RootExecutionLegId,
        saved.AssignmentRevision,
        fuel.IssueState ?? FuelIssueStates.HosUnknown,
        fuel.IssueHorizonEndsAt,
        fuel.IssueCritical,
        visits
          .Select(x => new FuelIssueLine(
            FuelVisitIdentity.Key(x.Stop),
            x.Text,
            x.Stop.Sent is { Changed: false } sent
              && sent.Delivery != DriverMessageStatuses.Failed,
            x.Stop.Sent is { Changed: true }
          )
          {
            Delivery = x.Stop.Sent?.Delivery,
          })
          .ToList(),
        FuelIssueMessage.Compose(visits.Select(x => x.Text).ToList())
      )
      {
        Recipient = await channel.RecipientAsync(plan.TruckId, ct),
        LastMessage = await channel.LatestAsync(saved, ct),
      },
      visits
    );
  }
}

public sealed class GetFuelIssuePreviewHandler(FuelIssuePreviews previews)
  : IRequestHandler<GetFuelIssuePreviewQuery, RequestResponse<FuelIssuePreview>>
{
  public async Task<RequestResponse<FuelIssuePreview>> Handle(
    GetFuelIssuePreviewQuery request,
    CancellationToken ct
  ) =>
    await previews.ReadAsync(request.DispatchId, request.ExecutionLegId, ct)
      is { } current
      ? RequestResponse<FuelIssuePreview>.Ok(current.Preview)
      : RequestResponse<FuelIssuePreview>.Fail(
        "There is no fuel plan to hand over for this load.",
        404
      );
}

public sealed class ConfirmFuelIssueSentHandler(
  FuelIssuePreviews previews,
  FuelIssueRecords records,
  ICurrentUser caller
)
  : IRequestHandler<
    ConfirmFuelIssueSentCommand,
    RequestResponse<FuelIssuePreview>
  >
{
  public async Task<RequestResponse<FuelIssuePreview>> Handle(
    ConfirmFuelIssueSentCommand request,
    CancellationToken ct
  )
  {
    var sent = request.Sent;
    var current = await previews.ReadAsync(
      request.DispatchId,
      sent.ExecutionLegId,
      ct
    );
    if (current is null)
      return RequestResponse<FuelIssuePreview>.Fail(
        "There is no fuel plan to hand over for this load.",
        404
      );
    // What was handed over has to be what the plan says now: the same
    // calculation, the same assignment, and each confirmed stop still one
    // of this shift's.
    var byKey = current.Visits.ToDictionary(x => FuelVisitIdentity.Key(x.Stop));
    if (Moved(current.Saved, sent, byKey.Keys.ToHashSet()))
      return RequestResponse<FuelIssuePreview>.Fail(
        "The fuel plan changed since it was opened. Open it again, send the new plan, then confirm.",
        409
      );
    await records.RecordAsync(
      current.Saved,
      sent.VisitKeys.Distinct().Select(key => byKey[key]).ToList(),
      FuelSendChannels.Manual,
      caller.IdentityUserId,
      ct
    );
    var after = await previews.ReadAsync(
      request.DispatchId,
      sent.ExecutionLegId,
      ct
    );
    return RequestResponse<FuelIssuePreview>.Ok(
      after?.Preview ?? current.Preview
    );
  }

  // The plan moved between the preview and the confirmation: another
  // calculation, another assignment, or a confirmed stop that is no longer
  // one of this shift's.
  public static bool Moved(
    TruckFuelPlanSnapshot saved,
    FuelIssueSentRequest sent,
    IReadOnlySet<string> current
  ) =>
    saved.CalculatedAt != sent.ExpectedCalculatedAt
    || sent.ExecutionLegId is { } leg && saved.RootExecutionLegId != leg
    || sent.AssignmentRevision is { } revision
      && saved.AssignmentRevision != revision
    || sent.VisitKeys.Any(key => !current.Contains(key));
}
