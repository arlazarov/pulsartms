using System.Text.Json;
using Application.Features.Dispatch.Models;
using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Commands.SyncDispatche;

internal static class DispatchStopMatcher
{
  private sealed record Place(
    string Job,
    StopAddress Address,
    string Name,
    decimal? Latitude,
    decimal? Longitude
  );

  private sealed record Appointment(
    string Reference,
    DateOnly? Date,
    TimeOnly? Time,
    DateOnly? EndDate,
    TimeOnly? EndTime,
    bool Window
  );

  public static IReadOnlyDictionary<ExternalDispatchStop, DispatchStop> Match(
    IReadOnlyCollection<DispatchStop> existing,
    IReadOnlyCollection<ExternalDispatchStop> incoming
  )
  {
    var result = new Dictionary<ExternalDispatchStop, DispatchStop>();
    var groups = existing
      .GroupBy(Identity)
      .ToDictionary(g => g.Key, g => g.ToArray());
    foreach (var sourceGroup in incoming.GroupBy(Identity))
    {
      if (!groups.TryGetValue(sourceGroup.Key, out var saved))
        continue;
      var source = sourceGroup.ToArray();
      if (saved.Length == 1 && source.Length == 1)
      {
        result.Add(source[0], saved[0]);
        continue;
      }

      var appointments = saved
        .GroupBy(Schedule)
        .ToDictionary(g => g.Key, g => g.ToArray());
      foreach (var visits in source.GroupBy(Schedule))
      {
        if (!appointments.TryGetValue(visits.Key, out var candidates))
          continue;
        var replacements = visits.ToArray();
        if (candidates.Length == 1 && replacements.Length == 1)
          result.Add(replacements[0], candidates[0]);
        // Indistinguishable repeat visits retain IDs only when their slots are
        // unchanged.
        else if (
          candidates.Length == replacements.Length
          && candidates.Select(s => s.Sequence).Distinct().Count()
            == candidates.Length
          && candidates
            .Select(s => s.Sequence)
            .Order()
            .SequenceEqual(replacements.Select(s => s.Sequence).Order())
        )
        {
          var bySequence = candidates.ToDictionary(s => s.Sequence);
          foreach (var visit in replacements)
            result.Add(visit, bySequence[visit.Sequence]);
        }
      }
    }
    return result;
  }

  private static Place Identity(DispatchStop stop)
  {
    // Verified coordinates and addresses must not change the provider's visit
    // identity.
    var address = string.IsNullOrWhiteSpace(stop.SourceAddressJson)
      ? StopAddress.From(stop)
      : JsonSerializer.Deserialize<StopAddress>(stop.SourceAddressJson)
        ?? StopAddress.From(stop);
    return Identity(
      stop.Job,
      address,
      stop.Name,
      stop.Latitude,
      stop.Longitude
    );
  }

  private static Place Identity(ExternalDispatchStop stop) =>
    Identity(
      stop.Job,
      new(stop.Address, stop.City, stop.Province, stop.Country, stop.ZipCode),
      stop.Name,
      stop.Latitude,
      stop.Longitude
    );

  private static Place Identity(
    string job,
    StopAddress address,
    string name,
    decimal? latitude,
    decimal? longitude
  )
  {
    var normalized = new StopAddress(
      Normalize(address.Address),
      Normalize(address.City),
      Normalize(address.Province),
      Normalize(address.Country),
      Normalize(address.ZipCode)
    );
    var located =
      normalized.Address.Length > 0
      || normalized.City.Length > 0
      || normalized.ZipCode.Length > 0;
    return new(
      Normalize(job),
      normalized,
      located ? "" : Normalize(name),
      located ? null : latitude,
      located ? null : longitude
    );
  }

  private static Appointment Schedule(DispatchStop stop) =>
    new(
      Normalize(stop.StopNo),
      stop.ScheduledDate,
      stop.ScheduledTime,
      stop.ScheduledDate2,
      stop.ScheduledTime2,
      stop.IsWindow
    );

  private static Appointment Schedule(ExternalDispatchStop stop) =>
    new(
      Normalize(stop.StopNo),
      stop.ScheduledDate,
      stop.ScheduledTime,
      stop.ScheduledDate2,
      stop.ScheduledTime2,
      stop.IsWindow
    );

  private static string Normalize(string value) =>
    string.Join(
        ' ',
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
      )
      .ToUpperInvariant();
}
