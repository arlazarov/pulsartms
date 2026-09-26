using Application.Features.Messaging.Services;
using Application.Models;

namespace Application.Features.Messaging.Queries;

// "Which conversations changed?" for the caller's company, answered as soon
// as a change commits, or empty after MessagingMailboxes.Wait. The answer
// names the mailbox to ask with next; see MessagingMailboxes.
public sealed record WaitMessagingChangesQuery(Guid? Mailbox)
  : IRequest<RequestResponse<MessagingChanges>>;

public sealed class WaitMessagingChangesHandler(
  ICurrentUser caller,
  ICurrentCompany company,
  MessagingMailboxes mailboxes
)
  : IRequestHandler<
    WaitMessagingChangesQuery,
    RequestResponse<MessagingChanges>
  >
{
  public async Task<RequestResponse<MessagingChanges>> Handle(
    WaitMessagingChangesQuery request,
    CancellationToken ct
  ) =>
    company.Id is { } serving && caller.IdentityUserId is { } account
      ? RequestResponse<MessagingChanges>.Ok(
        await mailboxes.WaitAsync(serving, account, request.Mailbox, ct)
      )
      : RequestResponse<MessagingChanges>.Fail("Access denied.", 403);
}
