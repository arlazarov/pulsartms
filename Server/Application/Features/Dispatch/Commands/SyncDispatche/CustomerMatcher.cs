using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Commands.SyncDispatche;

public static class CustomerMatcher
{
  public static Customer? Match(IEnumerable<Customer> customers, string name)
  {
    var normalized = Normalize(name);

    if (string.IsNullOrEmpty(normalized))
      return null;

    return customers.FirstOrDefault(x => x.NormalizedName == normalized);
  }

  public static Customer Create(string name)
  {
    var trimmed = name.Trim();

    return new Customer
    {
      Id = Guid.NewGuid(),
      Name = trimmed,
      NormalizedName = Normalize(trimmed),
    };
  }

  public static string Normalize(string value)
  {
    return new string([.. value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
  }
}
