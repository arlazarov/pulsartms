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
) : IRequest<RequestResponse<RouteChoiceDisplayPreview>>, IPlanningRequest;

public sealed record SaveRouteChoiceCommand(
  Guid DispatchId,
  RouteChoiceSave Request
) : IRequest<RequestResponse<long>>, IPlanningRequest;

public sealed record LocateRouteViaCommand(string Address)
  : IRequest<RequestResponse<RoutePoint>>,
    IPlanningRequest;

public sealed class PreviewRouteChoiceValidator
  : AbstractValidator<PreviewRouteChoiceCommand>
{
  public PreviewRouteChoiceValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
    RuleFor(x => x.Request).NotNull();
    When(
      x => x.Request is not null,
      () =>
      {
        RuleFor(x => x.Request.ViaPoints)
          .NotNull()
          .Must(x => x is null || x.Count <= 20);
        RuleForEach(x => x.Request.ViaPoints).NotNull();
      }
    );
  }
}

public sealed class SaveRouteChoiceValidator
  : AbstractValidator<SaveRouteChoiceCommand>
{
  public SaveRouteChoiceValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
    RuleFor(x => x.Request).NotNull();
    When(
      x => x.Request is not null,
      () =>
      {
        RuleFor(x => x.Request.PreviewId).NotEmpty();
        RuleFor(x => x.Request.Option).InclusiveBetween(1, 3);
        RuleFor(x => x.Request.Revision).InclusiveBetween(0, long.MaxValue - 1);
      }
    );
  }
}

public sealed class LocateRouteViaValidator
  : AbstractValidator<LocateRouteViaCommand>
{
  public LocateRouteViaValidator() =>
    RuleFor(x => x.Address).NotEmpty().MaximumLength(300);
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
