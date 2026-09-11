using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Services;
using Application.Models;

namespace Application.Features.Fuel.Commands;

public record StartGmailWatchCommand : IRequest<RequestResponse<GmailWatchResult>>;

public class StartGmailWatchHandler(GmailWatchLifecycle lifecycle)
  : IRequestHandler<StartGmailWatchCommand, RequestResponse<GmailWatchResult>>
{
  public async Task<RequestResponse<GmailWatchResult>> Handle(
    StartGmailWatchCommand request,
    CancellationToken cancellationToken
  )
  {
    var result = await lifecycle.RunAsync(true, cancellationToken);
    if (result.Busy) return RequestResponse<GmailWatchResult>.Fail("Gmail watch maintenance is already in progress.", 409);
    return result.Watch is { } watch ? RequestResponse<GmailWatchResult>.Ok(watch)
      : RequestResponse<GmailWatchResult>.Fail("Gmail watch renewal failed. Automatic retry is scheduled; verify mailbox credentials and Pub/Sub configuration.", 503);
  }
}
