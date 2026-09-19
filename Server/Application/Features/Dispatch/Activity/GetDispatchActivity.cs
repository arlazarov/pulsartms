using Application.Models;

namespace Application.Features.Dispatch.Activity;

public sealed record GetDispatchActivityQuery(
  Guid DispatchId,
  long? BeforeRevision = null,
  bool OpenOnly = false
) : IRequest<RequestResponse<DispatchActivityPage>>;

public sealed class GetDispatchActivityHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
)
  : IRequestHandler<
    GetDispatchActivityQuery,
    RequestResponse<DispatchActivityPage>
  >
{
  public const int PageSize = 10;

  public async Task<RequestResponse<DispatchActivityPage>> Handle(
    GetDispatchActivityQuery request,
    CancellationToken ct
  )
  {
    if (await DispatchActivityAccess.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<DispatchActivityPage>.Fail("Forbidden.", 403);
    if (request.BeforeRevision is <= 0)
      return RequestResponse<DispatchActivityPage>.Fail("Invalid page.");
    if (!await db.Dispatches.AnyAsync(x => x.Id == request.DispatchId, ct))
      return RequestResponse<DispatchActivityPage>.Fail("Load not found.", 404);
    var revision =
      await db
        .DispatchActivityThreads.AsNoTracking()
        .Where(x => x.Id == request.DispatchId)
        .Select(x => (long?)x.Revision)
        .SingleOrDefaultAsync(ct) ?? 0;
    var all = db
      .DispatchActivityEntries.AsNoTracking()
      .Where(x => x.DispatchId == request.DispatchId);
    var open = all.Where(x => x.NeedsAttention && x.ResolvedAt == null);
    var count = await open.CountAsync(ct);
    var selected = request.OpenOnly ? open : all;
    if (request.BeforeRevision is { } before)
      selected = selected.Where(x => x.CreatedRevision < before);
    var page = await selected
      .OrderByDescending(x => x.CreatedRevision)
      .Take(PageSize + 1)
      .Select(DispatchActivityAccess.Projection)
      .ToListAsync(ct);
    var openPage = request.OpenOnly
      ? page
      : await open.OrderByDescending(x => x.CreatedRevision)
        .Take(PageSize + 1)
        .Select(DispatchActivityAccess.Projection)
        .ToListAsync(ct);
    return RequestResponse<DispatchActivityPage>.Ok(
      new(
        request.DispatchId,
        revision,
        page.Take(PageSize).ToList(),
        page.Count > PageSize ? page[PageSize - 1].CreatedRevision : null,
        count,
        openPage.Take(PageSize).ToList(),
        openPage.Count > PageSize
          ? openPage[PageSize - 1].CreatedRevision
          : null
      )
    );
  }
}
