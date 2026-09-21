using Application.Features.Execution.Services;
using Application.Models;
using Domain.Models.Execution;

namespace Application.Features.Execution.Queries;

public sealed record GetExecutionSourceReviewQuery(
  Guid DispatchId,
  Guid ExecutionLegId
) : IRequest<RequestResponse<ExecutionSourceReview>>;

public sealed class GetExecutionSourceReviewHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    GetExecutionSourceReviewQuery,
    RequestResponse<ExecutionSourceReview>
  >
{
  public async Task<RequestResponse<ExecutionSourceReview>> Handle(
    GetExecutionSourceReviewQuery request,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<ExecutionSourceReview>.Fail("Access denied.", 403);
    var state = await ExecutionSourceReviewReader.ReadAsync(
      db,
      request.DispatchId,
      request.ExecutionLegId,
      clock.GetUtcNow().UtcDateTime,
      ct
    );
    return state is null
      ? RequestResponse<ExecutionSourceReview>.Fail(
        "Assignment not found.",
        404
      )
      : RequestResponse<ExecutionSourceReview>.Ok(state.Review);
  }
}
