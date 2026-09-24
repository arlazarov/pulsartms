using Application.Models;

namespace Application.Features.Messaging.Queries;

// Who a conversation is with: the driver matched by number or chosen by a
// dispatcher, or nobody. What that driver is driving belongs to Execution
// (GetDriverWorkQuery); the API puts the two answers side by side.
public sealed record GetConversationDriverQuery(Guid Id)
  : IRequest<RequestResponse<ConversationDriver>>;

public sealed record ConversationDriver(Guid? DriverId, string? DriverName);

public sealed class ConversationDriverHandler(
  IAppDbContext db,
  ICurrentUser caller
)
  : IRequestHandler<
    GetConversationDriverQuery,
    RequestResponse<ConversationDriver>
  >
{
  public async Task<RequestResponse<ConversationDriver>> Handle(
    GetConversationDriverQuery request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is null)
      return RequestResponse<ConversationDriver>.Fail("Access denied.", 403);
    var driver = await db
      .Conversations.AsNoTracking()
      .Where(x => x.Id == request.Id)
      .Select(x => new ConversationDriver(
        x.DriverId,
        db.Drivers.Where(d => d.Id == x.DriverId)
          .Select(d => d.Name)
          .FirstOrDefault()
      ))
      .SingleOrDefaultAsync(ct);
    return driver is null
      ? RequestResponse<ConversationDriver>.Fail("Conversation not found.", 404)
      : RequestResponse<ConversationDriver>.Ok(driver);
  }
}
