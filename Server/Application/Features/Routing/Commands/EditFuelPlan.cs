using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Models;

namespace Application.Features.Routing.Commands;

public sealed record EditFuelPlanCommand(
  Guid DispatchId,
  FuelPlanEditRequest Edit,
  bool Save
) : IRequest<RequestResponse<FuelPlanEditPreview>>, IPlanningRequest;

public sealed class EditFuelPlanValidator
  : AbstractValidator<EditFuelPlanCommand>
{
  public EditFuelPlanValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
    RuleFor(x => x.Edit).NotNull();
    When(
      x => x.Edit is not null,
      () =>
      {
        RuleFor(x => x.Edit.ExpectedCalculatedAt)
          .Must(x =>
            x is null || x.Value.Kind == DateTimeKind.Utc && x.Value != default
          )
          .WithMessage("Reopen the current fuel plan before saving.");
        RuleFor(x => x.Edit.Stops).NotNull().When(x => x.Save);
        RuleFor(x => x.Edit.Stops)
          .Must(x =>
            x is null || x.Count <= 40 && x.All(stop => stop is not null)
          )
          .WithMessage("A fuel plan can contain at most 40 valid stops.");
      }
    );
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
) : IRequest<RequestResponse<AutomaticPlanningResult>>, IPlanningRequest;

public sealed class ResetFuelPlanValidator
  : AbstractValidator<ResetFuelPlanCommand>
{
  public ResetFuelPlanValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
    RuleFor(x => x.Reset).NotNull();
    When(
      x => x.Reset is not null,
      () =>
        RuleFor(x => x.Reset.ExpectedCalculatedAt)
          .Must(x =>
            x is null || x.Value.Kind == DateTimeKind.Utc && x.Value != default
          )
          .WithMessage("Reopen the current fuel plan before resetting.")
    );
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
