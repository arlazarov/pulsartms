using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Commands;

public sealed record EditFuelPlanCommand(
  Guid DispatchId,
  FuelPlanEditRequest Edit,
  bool Save
) : IRequest<RequestResponse<FuelPlanEditPreview>>, IPlanningRequest, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
    if (Edit is null)
      yield return "The fuel plan to save is missing.";
    else
    {
      if (
        Edit.ExpectedCalculatedAt is { } at
        && (at.Kind != DateTimeKind.Utc || at == default)
      )
        yield return "Reopen the current fuel plan before saving.";
      if (Edit.Stops is null && Save)
        yield return "The fuel stops to save are missing.";
      if (
        Edit.Stops is { } stops
        && (stops.Count > 40 || stops.Any(stop => stop is null))
      )
        yield return "A fuel plan can contain at most 40 valid stops.";
    }
  }
}

public sealed class EditFuelPlanHandler(FuelPlanningService fuel)
  : IRequestHandler<EditFuelPlanCommand, RequestResponse<FuelPlanEditPreview>>
{
  public async Task<RequestResponse<FuelPlanEditPreview>> Handle(
    EditFuelPlanCommand request,
    CancellationToken ct
  ) =>
    RequestResponse<FuelPlanEditPreview>.Ok(
      await fuel.EditAsync(request.DispatchId, request.Edit, request.Save, ct)
    );
}

public sealed record ResetFuelPlanRequest(DateTime? ExpectedCalculatedAt)
{
  public Guid? ExecutionLegId { get; init; }
  public long? AssignmentRevision { get; init; }
}

public sealed record ResetFuelPlanCommand(
  Guid DispatchId,
  ResetFuelPlanRequest Reset
)
  : IRequest<RequestResponse<AutomaticPlanningResult>>,
    IPlanningRequest,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
    if (Reset is null)
      yield return "The reset to apply is missing.";
    else if (
      Reset.ExpectedCalculatedAt is { } at
      && (at.Kind != DateTimeKind.Utc || at == default)
    )
      yield return "Reopen the current fuel plan before resetting.";
  }
}

public sealed class ResetFuelPlanHandler(
  FuelPlanningService fuel,
  PlanningReadService reads
)
  : IRequestHandler<
    ResetFuelPlanCommand,
    RequestResponse<AutomaticPlanningResult>
  >
{
  public async Task<RequestResponse<AutomaticPlanningResult>> Handle(
    ResetFuelPlanCommand request,
    CancellationToken ct
  )
  {
    var calculation = await fuel.ResetAsync(
      request.DispatchId,
      request.Reset.ExpectedCalculatedAt,
      ct,
      request.Reset.ExecutionLegId,
      request.Reset.AssignmentRevision
    );
    return RequestResponse<AutomaticPlanningResult>.Ok(
      (
        await reads.ForDispatchAsync(
          request.DispatchId,
          ct,
          executionLegId: request.Reset.ExecutionLegId
        )
      ) with
      {
        FuelStatus = calculation.Status,
        Message = calculation.Access?.Message,
      }
    );
  }
}
