using Application.Caching;
using Application.Features.DriverGroups.Queries;
using Application.Features.DriverGroups.Services;
using Application.Models;
using Domain.Entities.DriverGroups;

namespace Application.Features.DriverGroups.Commands;

// Create (Id null) or change one of one's own groups: its name and its
// drivers, at the revision the dispatcher saw. Drivers must be the
// company's; a group may be empty and may share drivers with others.
public sealed record SaveDriverGroupCommand(
  Guid? Id,
  string Name,
  IReadOnlyList<Guid> Drivers,
  long Revision
) : IRequest<RequestResponse<DriverGroupView>>;

// Removes the group and its membership rows only: no driver, message or
// load is touched. Whoever had it chosen is back on all drivers.
public sealed record DeleteDriverGroupCommand(Guid Id)
  : IRequest<RequestResponse<bool>>;

// The one choice every page follows; null is all drivers.
public sealed record SelectDriverGroupCommand(Guid? Id)
  : IRequest<RequestResponse<bool>>;

public sealed class DriverGroupHandlers(
  IAppDbContext db,
  ICurrentUser caller,
  ReadCache reads,
  TimeProvider clock
)
  : IRequestHandler<SaveDriverGroupCommand, RequestResponse<DriverGroupView>>,
    IRequestHandler<DeleteDriverGroupCommand, RequestResponse<bool>>,
    IRequestHandler<SelectDriverGroupCommand, RequestResponse<bool>>
{
  public const int MaximumName = 60;
  public const int MaximumDrivers = 500;

  public async Task<RequestResponse<DriverGroupView>> Handle(
    SaveDriverGroupCommand request,
    CancellationToken ct
  )
  {
    if (await DriverGroupOwner.UserAsync(db, caller, ct) is not { } user)
      return Fail("Access denied.", 403);
    var name = request.Name?.Trim() ?? "";
    if (name.Length is 0 or > MaximumName)
      return Fail($"Name the group in at most {MaximumName} characters.", 400);
    var drivers = (request.Drivers ?? []).Distinct().ToArray();
    if (drivers.Length > MaximumDrivers)
      return Fail($"A group holds at most {MaximumDrivers} drivers.", 400);
    var known = await db
      .Drivers.AsNoTracking()
      .Where(x => drivers.Contains(x.Id))
      .CountAsync(ct);
    if (known != drivers.Length)
      return Fail("One of the drivers chosen is not in this company.", 400);
    if (
      await db.DriverGroups.AnyAsync(
        x => x.OwnerUserId == user && x.Name == name && x.Id != request.Id,
        ct
      )
    )
      return Fail("You already have a group with this name.", 409);
    var now = clock.GetUtcNow().UtcDateTime;
    DriverGroup group;
    if (request.Id is { } id)
    {
      var found = await db.DriverGroups.SingleOrDefaultAsync(
        x => x.Id == id && x.OwnerUserId == user,
        ct
      );
      if (found is null)
        return Fail("Group not found.", 404);
      if (found.Revision != request.Revision)
        return Fail(
          "This group changed in another window. Open it again.",
          409
        );
      group = found;
      group.Name = name;
      group.Revision++;
      group.UpdatedAt = now;
      // One save for the name and the members, under the revision: a
      // concurrent edit that wins leaves this one changing nothing.
      var members = await db
        .DriverGroupMembers.Where(x => x.GroupId == group.Id)
        .ToListAsync(ct);
      db.DriverGroupMembers.RemoveRange(
        members.Where(x => !drivers.Contains(x.DriverId))
      );
      drivers = [.. drivers.Except(members.Select(x => x.DriverId))];
    }
    else
    {
      group = new DriverGroup
      {
        Id = Guid.NewGuid(),
        OwnerUserId = user,
        Name = name,
        Revision = 1,
        CreatedAt = now,
        UpdatedAt = now,
      };
      db.DriverGroups.Add(group);
    }
    db.DriverGroupMembers.AddRange(
      drivers.Select(driver => new DriverGroupMember
      {
        Id = Guid.NewGuid(),
        GroupId = group.Id,
        DriverId = driver,
      })
    );
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException)
    {
      return Fail("This group changed in another window. Open it again.", 409);
    }
    reads.Invalidate(ReadGroups.DriverGroups);
    return RequestResponse<DriverGroupView>.Ok(
      new(
        group.Id,
        group.Name,
        group.Revision,
        await db
          .DriverGroupMembers.AsNoTracking()
          .Where(x => x.GroupId == group.Id)
          .Select(x => x.DriverId)
          .ToListAsync(ct)
      )
    );
  }

  public async Task<RequestResponse<bool>> Handle(
    DeleteDriverGroupCommand request,
    CancellationToken ct
  )
  {
    if (await DriverGroupOwner.UserAsync(db, caller, ct) is not { } user)
      return RequestResponse<bool>.Fail("Access denied.", 403);
    var removed = await db
      .DriverGroups.Where(x => x.Id == request.Id && x.OwnerUserId == user)
      .ExecuteDeleteAsync(ct);
    if (removed > 0)
      reads.Invalidate(ReadGroups.DriverGroups);
    return removed == 0
      ? RequestResponse<bool>.Fail("Group not found.", 404)
      : RequestResponse<bool>.Ok(true);
  }

  public async Task<RequestResponse<bool>> Handle(
    SelectDriverGroupCommand request,
    CancellationToken ct
  )
  {
    if (await DriverGroupOwner.UserAsync(db, caller, ct) is not { } user)
      return RequestResponse<bool>.Fail("Access denied.", 403);
    if (
      request.Id is { } id
      && !await db.DriverGroups.AnyAsync(
        x => x.Id == id && x.OwnerUserId == user,
        ct
      )
    )
      return RequestResponse<bool>.Fail("Group not found.", 404);
    await db
      .Users.Where(x => x.Id == user)
      .ExecuteUpdateAsync(
        x => x.SetProperty(u => u.SelectedDriverGroupId, request.Id),
        ct
      );
    reads.Invalidate(ReadGroups.DriverGroups);
    return RequestResponse<bool>.Ok(true);
  }

  private static RequestResponse<DriverGroupView> Fail(
    string message,
    int status
  ) => RequestResponse<DriverGroupView>.Fail(message, status);
}
