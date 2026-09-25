using System.Text.RegularExpressions;
using Application.Features.Messaging.Interfaces;
using Domain.Entities.Messaging;

namespace Application.Features.Messaging.Services;

// The templates approved for the serving company's business numbers. A
// dispatcher is offered those of the number the company sends from now; a
// template is queued and sent only while it is approved for the number its
// conversation is on.
public sealed partial class ApprovedTemplates(
  IAppDbContext db,
  IDriverMessaging messaging
)
{
  public const int MaximumParameters = 10;
  public const int MaximumText = 1024;

  public async Task<IReadOnlyList<ApprovedTemplate>> CurrentAsync(
    CancellationToken ct
  ) =>
    await messaging.BusinessNumberAsync(ct) is { } number
      ? await db
        .ApprovedTemplates.AsNoTracking()
        .Where(x =>
          x.Channel == messaging.Channel && x.BusinessNumberId == number
        )
        .OrderBy(x => x.Name)
        .ThenBy(x => x.Language)
        .ToListAsync(ct)
      : [];

  public Task<ApprovedTemplate?> FindAsync(
    string businessNumber,
    string name,
    string language,
    CancellationToken ct
  ) =>
    db
      .ApprovedTemplates.AsNoTracking()
      .Where(x =>
        x.Channel == messaging.Channel
        && x.BusinessNumberId == businessNumber
        && x.Name == name
        && x.Language == language
      )
      .SingleOrDefaultAsync(ct);

  // What the provider accepts as a name and a language, and a text whose
  // placeholders are exactly {{1}} to {{parameters}}, or named ones such as
  // {{company_name}} - as many distinct names as parameters, never mixed
  // with numbers.
  public static string? Refusal(
    string name,
    string language,
    int parameters,
    string text
  )
  {
    if (!Name().IsMatch(name))
      return "Enter the template name as Meta shows it: lower-case letters, "
        + "digits and underscores.";
    if (!Language().IsMatch(language))
      return "Enter the language code as Meta shows it, such as en_US.";
    if (parameters is < 0 or > MaximumParameters)
      return $"A template has at most {MaximumParameters} parameters.";
    if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumText)
      return $"Enter the template's text, at most {MaximumText} characters.";
    if (Names(text) is { } names)
      return names.Count == parameters && !Placeholder().IsMatch(text)
        ? null
        : "Use either numbered or named placeholders, as many as the "
          + "parameters.";
    var used = Placeholder()
      .Matches(text)
      .Select(x => int.Parse(x.Groups[1].Value))
      .ToHashSet();
    return used.SetEquals(Enumerable.Range(1, parameters))
      ? null
      : "The text must use {{1}} to {{"
        + parameters
        + "}}, each at least once, and no other placeholder.";
  }

  // A template's named parameters in the order they first appear, or null
  // when it uses numbered ones or none.
  public static IReadOnlyList<string>? Names(string text)
  {
    var names = NamedPlaceholder()
      .Matches(text)
      .Select(x => x.Groups[1].Value)
      .Distinct()
      .ToList();
    return names.Count == 0 ? null : names;
  }

  [GeneratedRegex("^[a-z0-9_]{1,512}$")]
  private static partial Regex Name();

  [GeneratedRegex(@"\{\{([a-z_][a-z0-9_]{0,63})\}\}")]
  private static partial Regex NamedPlaceholder();

  [GeneratedRegex("^[a-z]{2,3}(_[A-Z]{2})?$")]
  private static partial Regex Language();

  [GeneratedRegex(@"\{\{(\d{1,2})\}\}")]
  private static partial Regex Placeholder();
}
