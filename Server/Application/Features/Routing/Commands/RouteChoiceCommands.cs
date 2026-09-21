using Application.Diagnostics;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Commands;

public sealed record PreviewRouteChoiceCommand(
  Guid DispatchId,
  RouteChoiceRequest Request
)
  : IRequest<RequestResponse<RouteChoiceDisplayPreview>>,
    IPlanningRequest,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
    if (Request is null)
      yield return "The route request is missing.";
    else if (Request.ViaPoints is not { } via)
      yield return "The list of via points is missing.";
    else if (via.Count > 20 || via.Any(point => point is null))
      yield return "A route may pass through at most 20 via points.";
  }
}

public sealed record SaveRouteChoiceCommand(
  Guid DispatchId,
  RouteChoiceSave Request
) : IRequest<RequestResponse<long>>, IPlanningRequest, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
    if (Request is null)
      yield return "The route to save is missing.";
    else
    {
      if (Request.PreviewId == Guid.Empty)
        yield return "Preview the route before saving it.";
      if (Request.Option is < 1 or > 3)
        yield return "Choose one of the three routes.";
      if (Request.Revision is < 0 or long.MaxValue)
        yield return "Reopen the load before saving its route.";
    }
  }
}

public sealed record LocateRouteViaCommand(string Address)
  : IRequest<RequestResponse<RoutePoint>>,
    IPlanningRequest,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (string.IsNullOrWhiteSpace(Address) || Address.Length > 300)
      yield return "Enter an address of at most 300 characters.";
  }
}

public sealed class PreviewRouteChoiceHandler(
  RouteChoiceService choices,
  ICurrentUser caller,
  IAppDbContext db,
  IUserRoleService roles
)
  : IRequestHandler<
    PreviewRouteChoiceCommand,
    RequestResponse<RouteChoiceDisplayPreview>
  >
{
  public async Task<RequestResponse<RouteChoiceDisplayPreview>> Handle(
    PreviewRouteChoiceCommand request,
    CancellationToken ct
  )
  {
    if (!caller.IsAuthenticated || caller.IdentityUserId is null)
      return RequestResponse<RouteChoiceDisplayPreview>.Fail(
        "Unauthorized.",
        401
      );
    if (
      await roles.GetAsync(caller.IdentityUserId, ct)
      is not ("Admin" or "Dispatch")
    )
      return RequestResponse<RouteChoiceDisplayPreview>.Fail(
        "You cannot change routes.",
        403
      );
    var owner = await db
      .Users.Where(x => x.IsActive && x.IdentityUserId == caller.IdentityUserId)
      .Select(x => (Guid?)x.Id)
      .SingleOrDefaultAsync(ct);
    if (owner is null)
      return RequestResponse<RouteChoiceDisplayPreview>.Fail(
        "Unauthorized.",
        401
      );
    var preview = await choices.PreviewAsync(
      request.DispatchId,
      owner.Value,
      request.Request,
      ct
    );
    using var stage = PerformanceStages.Start(
      "route-options",
      "display-projection"
    );
    var display = RouteChoiceDisplay.Create(preview);
    PerformanceStages.Count(
      "route-options",
      "original-leg-points",
      preview.Options.Sum(option =>
        (long)option.Route.Legs.Sum(leg => leg.Points.Count)
      )
    );
    PerformanceStages.Count(
      "route-options",
      "display-leg-points",
      display.Options.Sum(option =>
        (long)option.Route.Legs.Sum(leg => leg.Points.Count)
      )
    );
    return RequestResponse<RouteChoiceDisplayPreview>.Ok(display);
  }
}

public sealed class SaveRouteChoiceHandler(
  RouteChoiceService choices,
  ICurrentUser caller,
  IAppDbContext db,
  IUserRoleService roles
) : IRequestHandler<SaveRouteChoiceCommand, RequestResponse<long>>
{
  public async Task<RequestResponse<long>> Handle(
    SaveRouteChoiceCommand request,
    CancellationToken ct
  )
  {
    if (!caller.IsAuthenticated || caller.IdentityUserId is null)
      return RequestResponse<long>.Fail("Unauthorized.", 401);
    if (
      await roles.GetAsync(caller.IdentityUserId, ct)
      is not ("Admin" or "Dispatch")
    )
      return RequestResponse<long>.Fail("You cannot change routes.", 403);
    var owner = await db
      .Users.Where(x => x.IsActive && x.IdentityUserId == caller.IdentityUserId)
      .Select(x => (Guid?)x.Id)
      .SingleOrDefaultAsync(ct);
    if (owner is null)
      return RequestResponse<long>.Fail("Unauthorized.", 401);
    try
    {
      return RequestResponse<long>.Ok(
        await choices.SaveAsync(
          request.DispatchId,
          owner.Value,
          request.Request,
          ct
        )
      );
    }
    catch (DbUpdateConcurrencyException)
    {
      return RequestResponse<long>.Fail(
        "The route changed. Refresh and try again.",
        409
      );
    }
  }
}

public sealed class LocateRouteViaHandler(
  IRoutingProvider routing,
  ICurrentUser caller,
  IUserRoleService roles
) : IRequestHandler<LocateRouteViaCommand, RequestResponse<RoutePoint>>
{
  public async Task<RequestResponse<RoutePoint>> Handle(
    LocateRouteViaCommand request,
    CancellationToken ct
  )
  {
    if (!caller.IsAuthenticated || caller.IdentityUserId is null)
      return RequestResponse<RoutePoint>.Fail("Unauthorized.", 401);
    if (
      await roles.GetAsync(caller.IdentityUserId, ct)
      is not ("Admin" or "Dispatch")
    )
      return RequestResponse<RoutePoint>.Fail("You cannot change routes.", 403);
    return RequestResponse<RoutePoint>.Ok(
      await routing.GeocodeAsync(request.Address, ct)
    );
  }
}
