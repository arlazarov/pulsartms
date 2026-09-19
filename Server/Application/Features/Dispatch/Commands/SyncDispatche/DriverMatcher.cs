using Domain.Entities.Fleet;

namespace Application.Features.Dispatch.Commands.SyncDispatche;

public static class DriverMatcher
{
  public sealed class Index(IEnumerable<Driver> drivers)
  {
    private readonly (Driver Driver, HashSet<string> Tokens)[] candidates =
      drivers.Select(x => (x, Tokenize(x.ImportedName ?? x.Name))).ToArray();
    private readonly Dictionary<string, Driver?> resolved = new(
      StringComparer.Ordinal
    );

    public Driver? Match(string name)
    {
      if (resolved.TryGetValue(name, out var saved))
        return saved;
      var source = Tokenize(name);
      Driver? result = null;
      if (source.Count > 0)
        foreach (var candidate in candidates)
        {
          if (
            candidate.Tokens.Count == 0
            || !candidate.Tokens.All(source.Contains)
          )
            continue;
          if (result is not null)
          {
            result = null;
            break;
          }
          result = candidate.Driver;
        }
      resolved[name] = result;
      return result;
    }
  }

  public static Driver? Match(IEnumerable<Driver> drivers, string name)
  {
    var sourceTokens = Tokenize(name);

    if (sourceTokens.Count == 0)
      return null;

    var matches = drivers
      .Where(x =>
      {
        var driverTokens = Tokenize(x.ImportedName ?? x.Name);

        return driverTokens.Count > 0
          && driverTokens.All(sourceTokens.Contains);
      })
      .Take(2)
      .ToList();

    return matches.Count == 1 ? matches[0] : null;
  }

  private static HashSet<string> Tokenize(string value)
  {
    var normalized = new string(
      [
        .. value.Select(x =>
          char.IsLetterOrDigit(x) ? char.ToLowerInvariant(x) : ' '
        ),
      ]
    );

    return [.. normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
  }
}
