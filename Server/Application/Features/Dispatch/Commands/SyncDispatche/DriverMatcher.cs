using Domain.Entities.Fleet;

namespace Application.Features.Dispatch.Commands.SyncDispatche;

public static class DriverMatcher
{
  public static Driver? Match(IEnumerable<Driver> drivers, string name)
  {
    var sourceTokens = Tokenize(name);

    if (sourceTokens.Count == 0)
      return null;

    var matches = drivers
      .Where(x =>
      {
        var driverTokens = Tokenize(x.Name);

        return driverTokens.Count > 0 && driverTokens.All(sourceTokens.Contains);
      })
      .Take(2)
      .ToList();

    return matches.Count == 1 ? matches[0] : null;
  }

  private static HashSet<string> Tokenize(string value)
  {
    var normalized = new string(
      [.. value.Select(x => char.IsLetterOrDigit(x) ? char.ToLowerInvariant(x) : ' ')]
    );

    return [.. normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
  }
}
