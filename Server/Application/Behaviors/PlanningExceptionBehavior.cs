using Application.Features.Routing.Commands;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Models;

namespace Application.Behaviors;

public sealed class PlanningExceptionBehavior<TRequest, TData>
  : IPipelineBehavior<TRequest, RequestResponse<TData>>
  where TRequest : IRequest<RequestResponse<TData>>
{
  public async Task<RequestResponse<TData>> Handle(
    TRequest request,
    RequestHandlerDelegate<RequestResponse<TData>> next,
    CancellationToken cancellationToken
  )
  {
    try
    {
      return await next(cancellationToken);
    }
    catch (PlanningSettingsConflictException ex)
      when (request is IPlanningRequest)
    {
      return RequestResponse<TData>.Fail(ex.Message, 409);
    }
    catch (DbUpdateException) when (request is UpdatePlanningSettingsCommand)
    {
      return RequestResponse<TData>.Fail(
        "Settings could not be saved. Reload the latest settings and try again.",
        409
      );
    }
    catch (DbUpdateConcurrencyException) when (request is IPlanningRequest)
    {
      return RequestResponse<TData>.Fail(
        "The route changed in another session. Reload it before recalculating.",
        409
      );
    }
    catch (RoutePlanningException ex) when (request is IPlanningRequest)
    {
      return RequestResponse<TData>.Fail(ex.Message);
    }
  }
}
