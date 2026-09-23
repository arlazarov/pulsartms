using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Domain.Rules.Fleet;

namespace Application.Features.Fleet.Services;

// The one owner of which trailers exist. Every source that names a trailer
// comes here: the telemetry provider with its ids, and imported loads with
// only a number. A trailer is matched by the identity its source gave it,
// then by its number, and is created only when nothing matches.
//
// Numbers are unique per carrier, so a number names at most one trailer. A
// row is taken over by another identity only when that cannot join two
// different trailers: a row known only by number, or one whose previous id
// is gone from its provider, and never when both carry different VINs. A
// trailer that cannot be placed is left as it is and counted, not merged
// and not duplicated.
public static class TrailerCatalog
{
  public static async Task<int> ApplyAsync(
    IAppDbContext db,
    string source,
    IReadOnlyList<ExternalTrailer> trailers,
    CancellationToken ct
  )
  {
    var rows = await db.Trailers.ToListAsync(ct);
    var byId = rows.Where(x => x.ExternalId.Length > 0)
      .ToDictionary(x => x.ExternalId, StringComparer.OrdinalIgnoreCase);
    var byUnit = ByUnit(rows);
    var numbered = Numbers(rows);
    var feed = trailers
      .Select(x => x.ExternalId)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var unplaced = 0;
    foreach (var trailer in trailers)
    {
      var unit = TrailerUnits.Normalize(trailer.UnitNumber);
      var vin = TrailerUnits.Vin(trailer.Vin);
      var known =
        byId.GetValueOrDefault(trailer.ExternalId) is { } row
        && (row.Source is null || row.Source == source)
          ? row
          : null;
      if (unit is null)
      {
        if (known is not null)
          FleetConfigurationImport.Apply(
            known,
            known.ImportedVin ?? known.Vin,
            false
          );
        continue;
      }
      if (known is not null)
      {
        known.Source ??= source;
        var holder = byUnit.GetValueOrDefault(unit);
        if (holder is not null && holder != known)
        {
          // Renamed at the source to a number another trailer has here.
          unplaced++;
          continue;
        }
        Rename(known, trailer.UnitNumber.Trim(), byUnit, unit);
        FleetConfigurationImport.Apply(known, vin, trailer.IsActive);
        continue;
      }
      if (byId.ContainsKey(trailer.ExternalId))
      {
        // The id belongs to another source's trailer.
        unplaced++;
        continue;
      }
      if (byUnit.GetValueOrDefault(unit) is { } same)
      {
        if (!Adoptable(same, vin, source, feed))
        {
          unplaced++;
          continue;
        }
        byId.Remove(same.ExternalId);
        same.ExternalId = trailer.ExternalId;
        same.Source = source;
        same.ConfigurationRevision++;
        byId[same.ExternalId] = same;
        FleetConfigurationImport.Apply(same, vin, trailer.IsActive);
        continue;
      }
      if (numbered.Contains(unit))
      {
        // Two trailers here already share this number once normalized.
        unplaced++;
        continue;
      }
      var added = new Trailer
      {
        Id = Guid.NewGuid(),
        ExternalId = trailer.ExternalId,
        Source = source,
        UnitNumber = trailer.UnitNumber.Trim(),
        Vin = vin,
        IsActive = trailer.IsActive,
        ImportedVin = vin,
        ImportedIsActive = trailer.IsActive,
      };
      db.Trailers.Add(added);
      byId[added.ExternalId] = added;
      byUnit[unit] = added;
      numbered.Add(unit);
    }
    return unplaced;
  }

  // The trailers these numbers name, keyed by normalized number, creating
  // the ones no source has reported yet. A number without a digit ("TBD",
  // "N/A") is not taken for a trailer.
  public static async Task<Dictionary<string, Trailer>> EnsureAsync(
    IAppDbContext db,
    string source,
    IEnumerable<string?> numbers,
    CancellationToken ct
  )
  {
    var wanted = numbers
      .Select(x => (Text: x?.Trim() ?? "", Unit: TrailerUnits.Nameable(x)))
      .Where(x => x.Unit is not null)
      .GroupBy(x => x.Unit!)
      .ToDictionary(x => x.Key, x => x.First().Text);
    var found = new Dictionary<string, Trailer>(StringComparer.Ordinal);
    if (wanted.Count == 0)
      return found;
    var rows = await db.Trailers.ToListAsync(ct);
    var byUnit = ByUnit(rows);
    var numbered = Numbers(rows);
    foreach (var (unit, text) in wanted)
    {
      if (byUnit.GetValueOrDefault(unit) is { } existing)
      {
        found[unit] = existing;
        continue;
      }
      if (numbered.Contains(unit))
        continue;
      var added = new Trailer
      {
        Id = Guid.NewGuid(),
        ExternalId = "",
        Source = source,
        UnitNumber = text,
        IsActive = true,
        ImportedIsActive = true,
      };
      db.Trailers.Add(added);
      found[unit] = added;
    }
    return found;
  }

  public static Trailer? Find(
    IReadOnlyDictionary<string, Trailer> trailers,
    string? number
  ) =>
    TrailerUnits.Normalize(number) is { } unit
      ? trailers.GetValueOrDefault(unit)
      : null;

  private static bool Adoptable(
    Trailer row,
    string vin,
    string source,
    IReadOnlySet<string> feed
  ) =>
    !(row.Vin.Length > 0 && vin.Length > 0 && row.Vin != vin)
    && (
      row.ExternalId.Length == 0
      || row.Source is { } other && other != source
      || !feed.Contains(row.ExternalId)
    );

  private static void Rename(
    Trailer trailer,
    string text,
    Dictionary<string, Trailer> byUnit,
    string unit
  )
  {
    if (trailer.UnitNumber == text)
      return;
    if (TrailerUnits.Normalize(trailer.UnitNumber) is { } old)
      byUnit.Remove(old);
    trailer.UnitNumber = text;
    trailer.ConfigurationRevision++;
    byUnit[unit] = trailer;
  }

  private static HashSet<string> Numbers(IEnumerable<Trailer> rows) =>
    rows.Select(x => TrailerUnits.Normalize(x.UnitNumber))
      .OfType<string>()
      .ToHashSet(StringComparer.Ordinal);

  // Rows whose stored numbers collide once normalized are left out: such a
  // number is ambiguous and matches nothing.
  private static Dictionary<string, Trailer> ByUnit(
    IEnumerable<Trailer> rows
  ) =>
    rows.Select(x => (Row: x, Unit: TrailerUnits.Normalize(x.UnitNumber)))
      .Where(x => x.Unit is not null)
      .GroupBy(x => x.Unit!)
      .Where(x => x.Count() == 1)
      .ToDictionary(x => x.Key, x => x.Single().Row, StringComparer.Ordinal);
}
