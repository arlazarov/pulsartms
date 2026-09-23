using Application.Features.Fleet.Services;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Commands;

public sealed record SendFuelIssueCommand(
  Guid DispatchId,
  FuelIssueSendRequest Send
)
  : IRequest<RequestResponse<FuelIssuePreview>>,
    IPlanningRequest,
    IChecked,
    IAboutWork
{
  public Guid? Load => DispatchId;

  public IEnumerable<string> Wrong() =>
    Send is null
      ? ["The plan to send is missing."]
      : new ConfirmFuelIssueSentCommand(DispatchId, Send.Plan).Wrong();
}

public sealed class SendFuelIssueHandler(
  FuelIssuePreviews previews,
  FuelIssueSender sender,
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
) : IRequestHandler<SendFuelIssueCommand, RequestResponse<FuelIssuePreview>>
{
  public async Task<RequestResponse<FuelIssuePreview>> Handle(
    SendFuelIssueCommand request,
    CancellationToken ct
  )
  {
    if (!await DriverContacts.MayUseAsync(db, caller, roles, ct))
      return RequestResponse<FuelIssuePreview>.Fail(
        "You cannot send plans to drivers.",
        403
      );
    var leg = request.Send.Plan.ExecutionLegId;
    var outcome = await sender.SendAsync(
      request.Send,
      token => previews.ReadAsync(request.DispatchId, leg, token),
      caller.IdentityUserId,
      ct
    );
    if (outcome.Error is not null)
      return RequestResponse<FuelIssuePreview>.Fail(
        outcome.Error,
        outcome.Status
      );
    // What the dispatcher sees next says what became of the attempt:
    // accepted, refused or unknown.
    var after = await previews.ReadAsync(
      request.DispatchId,
      leg,
      CancellationToken.None
    );
    return after is null
      ? RequestResponse<FuelIssuePreview>.Fail(
        "There is no fuel plan to hand over for this load.",
        404
      )
      : RequestResponse<FuelIssuePreview>.Ok(after.Preview);
  }
}
