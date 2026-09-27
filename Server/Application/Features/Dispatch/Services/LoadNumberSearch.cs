using System.Globalization;
using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Services;

// The load number a dispatcher's search names: typed bare (1408) or as the
// screens show it, with the carrier's prefix (AMF1408, "amf 1408"). Every
// Dispatch search reads it here, so the board and the history agree.
public static class LoadNumberSearch
{
  // The carrier's prefix is read only for a search that starts with a
  // letter, the one kind that can carry it.
  public static async Task<int?> NumberAsync(
    IAppDbContext db,
    string? search,
    CancellationToken ct
  )
  {
    var text = search?.Trim() ?? "";
    if (text.Length == 0)
      return null;
    if (!char.IsLetter(text[0]))
      return Number(text, null);
    var prefix = await db
      .DispatchSettings.AsNoTracking()
      .Where(x => x.Id == DispatchSettings.SingletonId)
      .Select(x => x.LoadNumberPrefix)
      .SingleOrDefaultAsync(ct);
    return Number(text, prefix ?? DispatchSettings.DefaultLoadNumberPrefix);
  }

  public static int? Number(string search, string? prefix)
  {
    var text = search.Trim();
    if (
      !string.IsNullOrEmpty(prefix)
      && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
    )
      text = text[prefix.Length..].TrimStart(' ', '-');
    text = text.TrimStart('#');
    return
      text.Length > 0
      && int.TryParse(
        text,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var number
      )
      ? number
      : null;
  }
}
