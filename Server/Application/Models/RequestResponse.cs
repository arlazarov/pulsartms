using System.Text.Json.Serialization;
using FluentValidation.Results;

namespace Application.Models;

public interface IRequestOutcome
{
  bool Success { get; }
}

public record RequestResponse<T>(
  bool Success,
  T? Response,
  ValidationErrors? Errors = null,
  [property: JsonIgnore] int StatusCode = 200
) : IRequestOutcome
{
  public static RequestResponse<T> Ok(T response, int statusCode = 200) =>
    new(true, response, null, statusCode);

  public static RequestResponse<T> Fail(
    ValidationErrors errors,
    int statusCode = 400
  ) => new(false, default, errors, statusCode);

  public static RequestResponse<T> Fail(string error, int statusCode = 400) =>
    new(false, default, new ValidationErrors(error), statusCode);
}

public class ValidationErrors : List<string>
{
  public ValidationErrors() { }

  public ValidationErrors(string error) => Add(error);

  public ValidationErrors(IEnumerable<string> errors) => AddRange(errors);
}

public static partial class Extensions
{
  public static ValidationErrors ToRequestErrors(
    this List<ValidationFailure> errors
  )
  {
    return new ValidationErrors(
      [
        .. errors.Select(e =>
          string.IsNullOrWhiteSpace(e.PropertyName)
            ? e.ErrorMessage
            : $"{e.PropertyName}: {e.ErrorMessage}"
        ),
      ]
    );
  }
}
