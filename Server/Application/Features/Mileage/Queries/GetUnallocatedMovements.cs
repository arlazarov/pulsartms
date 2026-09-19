using Application.Features.Mileage.Models;
using Application.Features.Mileage.Services;
using Application.Models;

namespace Application.Features.Mileage.Queries;

public sealed record GetUnallocatedMovementsQuery(
  int Offset = 0,
  int Limit = 50
) : IRequest<RequestResponse<UnallocatedMileagePage>>;

public sealed class GetUnallocatedMovementsHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
)
  : IRequestHandler<
    GetUnallocatedMovementsQuery,
    RequestResponse<UnallocatedMileagePage>
  >
{
  public async Task<RequestResponse<UnallocatedMileagePage>> Handle(
    GetUnallocatedMovementsQuery request,
    CancellationToken ct
  )
  {
    if (await MileageAccess.ActorAsync(db, caller, roles, false, ct) is null)
      return RequestResponse<UnallocatedMileagePage>.Fail(
        "Access denied.",
        403
      );
    if (request.Offset is < 0 or > 100_000 || request.Limit is < 1 or > 100)
      return RequestResponse<UnallocatedMileagePage>.Fail(
        "Choose a page size between 1 and 100.",
        400
      );
    var rows = await db
      .Movements.AsNoTracking()
      .Where(x => x.AllocatedDispatchId == null && !x.PlannedSuperseded)
      .OrderByDescending(x => x.RecordedAt)
      .ThenBy(x => x.Id)
      .Skip(request.Offset)
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    var details = await MileageMovementView.WithNumbersAsync(
      db,
      rows.Take(request.Limit).Select(MileageMovementView.From).ToList(),
      ct
    );
    return RequestResponse<UnallocatedMileagePage>.Ok(
      new(details, rows.Count > request.Limit)
    );
  }
}
