using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;

namespace Application.Features.Routing.Commands;

public sealed record UpdatePlanningSettingsCommand(
  PlanningSettingsUpdate Settings
) : IRequest<RequestResponse<PlanningSettingsState>>, IPlanningRequest;

public sealed class UpdatePlanningSettingsValidator
  : AbstractValidator<UpdatePlanningSettingsCommand>
{
  public UpdatePlanningSettingsValidator()
  {
    RuleFor(x => x.Settings).NotNull();
    When(
      x => x.Settings is not null,
      () => RuleFor(x => x.Settings.Preferences).NotNull()
    );
  }
}

public sealed class UpdatePlanningSettingsHandler(
  PlanningSettingsService service
)
  : IRequestHandler<
    UpdatePlanningSettingsCommand,
    RequestResponse<PlanningSettingsState>
  >
{
  public async Task<RequestResponse<PlanningSettingsState>> Handle(
    UpdatePlanningSettingsCommand request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<PlanningSettingsState>.Ok(
      await service.SaveAsync(request.Settings, cancellationToken)
    );
}
