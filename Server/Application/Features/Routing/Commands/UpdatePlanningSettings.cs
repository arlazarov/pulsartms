using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Commands;

public sealed record UpdatePlanningSettingsCommand(
  PlanningSettingsUpdate Settings
) : IRequest<RequestResponse<PlanningSettingsState>>, IPlanningRequest, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (Settings is null)
      yield return "The settings to save are missing.";
    else if (Settings.Preferences is null)
      yield return "The planning preferences are missing.";
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
