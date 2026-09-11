using Application.Models;

namespace Application.Behaviors;

public class ValidationBehavior<TRequest, TData>(IEnumerable<IValidator<TRequest>> validators)
  : IPipelineBehavior<TRequest, RequestResponse<TData>>
  where TRequest : IRequest<RequestResponse<TData>>
{
  public async Task<RequestResponse<TData>> Handle(
    TRequest request,
    RequestHandlerDelegate<RequestResponse<TData>> next,
    CancellationToken cancellationToken
  )
  {
    if (!validators.Any())
      return await next(cancellationToken);

    var context = new ValidationContext<TRequest>(request);

    var results = await Task.WhenAll(
      validators.Select(x => x.ValidateAsync(context, cancellationToken))
    );

    var failures = results.SelectMany(x => x.Errors).Where(x => x is not null).ToList();

    if (failures.Count == 0)
      return await next(cancellationToken);

    return RequestResponse<TData>.Fail(failures.ToRequestErrors());
  }
}
