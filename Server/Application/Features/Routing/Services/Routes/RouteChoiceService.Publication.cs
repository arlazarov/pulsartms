namespace Application.Features.Routing.Services.Routes;

public sealed partial class RouteChoiceService
{
  private void InvalidateSavedRoute(Guid dispatch, Guid? executionLeg)
  {
    reads.Invalidate("dispatch");
    reads.Invalidate("board");
    reads.Invalidate("execution");
    reads.Invalidate("route-previews");
    plans.Invalidate(dispatch, executionLeg);
  }
}
