using Application.Features.Dispatch.Models;
using Application.Features.Routing.Interfaces;
using Application.Models;
using Domain.Models.Execution;

namespace Application.Features.Dispatch.Commands;

public sealed record VerifyDispatchAddressCommand(
  Guid? DispatchId,
  VerifyDispatchAddressRequest Request
) : IRequest<RequestResponse<VerifiedDispatchAddress>>, IPlanningRequest;

public sealed class VerifyDispatchAddressHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  IAddressGeocoder geocoder
)
  : IRequestHandler<
    VerifyDispatchAddressCommand,
    RequestResponse<VerifiedDispatchAddress>
  >
{
  public async Task<RequestResponse<VerifiedDispatchAddress>> Handle(
    VerifyDispatchAddressCommand command,
    CancellationToken ct
  )
  {
    var actor = await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return RequestResponse<VerifiedDispatchAddress>.Fail(
        "Access denied.",
        403
      );
    if (
      command.DispatchId.HasValue
      && !await db.Dispatches.AnyAsync(x => x.Id == command.DispatchId, ct)
    )
      return RequestResponse<VerifiedDispatchAddress>.Fail(
        "Load not found.",
        404
      );
    var request = command.Request;
    if (
      request is null
      || string.IsNullOrWhiteSpace(request.Address)
      || request.Address.Length > 500
      || request.City?.Length > 150
      || request.Province?.Length > 100
      || request.Country?.Length > 100
      || request.ZipCode?.Length > 50
    )
      return RequestResponse<VerifiedDispatchAddress>.Fail(
        "Provide a street address within the supported limits."
      );
    var query = string.Join(
      ", ",
      new[]
      {
        request.Address,
        request.City,
        request.Province,
        request.Country,
        request.ZipCode,
      }.Where(x => !string.IsNullOrWhiteSpace(x))
    );
    var result = await geocoder.ResolveAsync(query, ct);
    if (!result.Point.IsValid)
      return RequestResponse<VerifiedDispatchAddress>.Fail(
        "The address lookup did not return valid coordinates.",
        422
      );
    return RequestResponse<VerifiedDispatchAddress>.Ok(
      new(
        result.Address,
        result.City,
        result.Province,
        result.Country,
        result.ZipCode,
        decimal.Round((decimal)result.Point.Latitude, 7),
        decimal.Round((decimal)result.Point.Longitude, 7)
      )
    );
  }
}
