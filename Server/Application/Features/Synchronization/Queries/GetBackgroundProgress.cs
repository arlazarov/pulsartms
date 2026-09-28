using Application.Diagnostics;
using Application.Models;

namespace Application.Features.Synchronization.Queries;

// Which background operations of this instance are making progress. A
// report only: it never fails liveness or readiness (BackgroundProgress).
public sealed record GetBackgroundProgressQuery
  : IRequest<RequestResponse<IReadOnlyList<BackgroundProgress.Progress>>>;

public sealed class GetBackgroundProgressHandler(TimeProvider clock)
  : IRequestHandler<
    GetBackgroundProgressQuery,
    RequestResponse<IReadOnlyList<BackgroundProgress.Progress>>
  >
{
  public Task<
    RequestResponse<IReadOnlyList<BackgroundProgress.Progress>>
  > Handle(GetBackgroundProgressQuery request, CancellationToken ct) =>
    Task.FromResult(
      RequestResponse<IReadOnlyList<BackgroundProgress.Progress>>.Ok(
        BackgroundProgress.Read(clock.GetUtcNow())
      )
    );
}
