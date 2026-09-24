using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Application.Models;

namespace Application.Features.Messaging.Commands;

// A dispatcher says which driver a conversation is with, when the number
// matched no driver or the wrong one, or clears it. The change applies
// only to the revision the dispatcher saw, commits with a new revision,
// and is signalled after the commit.
public sealed record SetConversationDriverCommand(
  Guid Id,
  Guid? DriverId,
  long Revision
) : IRequest<RequestResponse<long>>;

public sealed class SetConversationDriverHandler(
  IAppDbContext db,
  ICurrentUser caller,
  ICurrentCompany company,
  MessagingEvents events
) : IRequestHandler<SetConversationDriverCommand, RequestResponse<long>>
{
  public async Task<RequestResponse<long>> Handle(
    SetConversationDriverCommand request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is null)
      return RequestResponse<long>.Fail("Access denied.", 403);
    if (
      request.DriverId is { } driver
      && !await db.Drivers.AnyAsync(x => x.Id == driver, ct)
    )
      return RequestResponse<long>.Fail("Driver not found.", 404);
    var conversation = await db.Conversations.SingleOrDefaultAsync(
      x => x.Id == request.Id,
      ct
    );
    if (conversation is null)
      return RequestResponse<long>.Fail("Conversation not found.", 404);
    if (conversation.Revision != request.Revision)
      return Stale();
    if (conversation.DriverId == request.DriverId)
      return RequestResponse<long>.Ok(conversation.Revision);
    conversation.DriverId = request.DriverId;
    conversation.Revision++;
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Stale();
    }
    if (company.Id is { } serving)
      events.Publish(serving, new(conversation.Id, conversation.Revision));
    return RequestResponse<long>.Ok(conversation.Revision);
  }

  private static RequestResponse<long> Stale() =>
    RequestResponse<long>.Fail(
      "The conversation changed. Read it again, then choose the driver.",
      409
    );
}
