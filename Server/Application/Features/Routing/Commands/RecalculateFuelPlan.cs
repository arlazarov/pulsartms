using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Commands;

public sealed record RecalculateFuelPlanCommand(
  Guid DispatchId,
  Guid? ExecutionLegId = null,
  long? AssignmentRevision = null
) : IRequest<RequestResponse<AutomaticPlanningResult>>, IPlanningRequest
{
  internal DateTime? AutomaticRefreshRevision { get; init; }
}

public sealed class RecalculateFuelPlanValidator
  : AbstractValidator<RecalculateFuelPlanCommand>
{
  public RecalculateFuelPlanValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
    When(
      x => x.ExecutionLegId.HasValue,
      () =>
      {
        RuleFor(x => x.ExecutionLegId).NotEqual(Guid.Empty);
        RuleFor(x => x.AssignmentRevision).NotNull().GreaterThan(0);
      }
    );
  }
}

public sealed class RecalculateFuelPlanHandler(AutomaticPlanningService service)
  : IRequestHandler<
    RecalculateFuelPlanCommand,
    RequestResponse<AutomaticPlanningResult>
  >
{
  public async Task<RequestResponse<AutomaticPlanningResult>> Handle(
    RecalculateFuelPlanCommand request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<AutomaticPlanningResult>.Ok(
      await service.RecalculateFuelAsync(
        request.DispatchId,
        cancellationToken,
        request.ExecutionLegId,
        request.AssignmentRevision,
        request.AutomaticRefreshRevision
      )
    );
}
