using System.Data;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;

namespace Application.Features.Dispatch.Commands;

public sealed record SaveBrokerProfileCommand(BrokerProfile Profile)
  : IRequest<RequestResponse<BrokerProfile>>;

public sealed class SaveBrokerProfileHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
) : IRequestHandler<SaveBrokerProfileCommand, RequestResponse<BrokerProfile>>
{
  public async Task<RequestResponse<BrokerProfile>> Handle(
    SaveBrokerProfileCommand command,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return Fail("Access denied.", 403);
    var problem = BrokerProfiles.Validate(command.Profile);
    if (problem is not null)
      return Fail(problem, 400);
    var profile = command.Profile;
    var key = CustomerMatcher.Normalize(profile.Name);
    if (key.Length == 0)
      return Fail("Provide the broker name.", 400);
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        ct
      );
      var row = await db.Customers.SingleOrDefaultAsync(
        x => x.Id == profile.Id,
        ct
      );
      if (row is null)
      {
        if (profile.Revision != 0)
          return Fail("Broker not found.", 404);
        if (await db.Customers.AnyAsync(x => x.NormalizedName == key, ct))
          return Fail("This broker already exists. Search and select it.");
        row = CustomerMatcher.Create(profile.Name);
        row.Id = profile.Id;
        db.Customers.Add(row);
      }
      else
      {
        if (row.ProfileRevision != profile.Revision)
          return Fail("Broker changed. Search again before saving.");
        if (row.NormalizedName != key)
          return Fail(
            "Keep this broker's identity. Select another broker "
              + "instead of renaming a different company.",
            400
          );
      }
      row.Name = profile.Name.Trim();
      row.ProfileRevision++;
      row.ProfileJson = DispatchWorkspaceData.Write(profile);
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      return RequestResponse<BrokerProfile>.Ok(BrokerProfiles.Read(row));
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail("Broker changed concurrently. Search again before saving.");
    }
  }

  private static RequestResponse<BrokerProfile> Fail(
    string message,
    int status = 409
  ) => RequestResponse<BrokerProfile>.Fail(message, status);
}
