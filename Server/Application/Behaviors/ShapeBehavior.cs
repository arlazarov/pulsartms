using Application.Models;

namespace Application.Behaviors;

// Refusing a malformed request before the handler runs. This used to be
// FluentValidation: a package, a registration that found validator classes
// by reflection, and thirty-four of them living beside the handlers they
// guarded. What it bought over asking the request itself was the early
// refusal, and that is kept here - a request that cannot answer this
// question passes straight through.
public sealed class ShapeBehavior<TRequest, TData>
  : IPipelineBehavior<TRequest, RequestResponse<TData>>
  where TRequest : IRequest<RequestResponse<TData>>
{
  public async Task<RequestResponse<TData>> Handle(
    TRequest request,
    RequestHandlerDelegate<RequestResponse<TData>> next,
    CancellationToken cancellationToken
  )
  {
    if (request is not IChecked checkable)
      return await next(cancellationToken);
    var wrong = checkable.Wrong().ToList();
    return wrong.Count == 0
      ? await next(cancellationToken)
      : RequestResponse<TData>.Fail(new ValidationErrors(wrong));
  }
}
