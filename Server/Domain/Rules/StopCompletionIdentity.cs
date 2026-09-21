using System.Security.Cryptography;
using System.Text.Json;

namespace Domain.Rules;

public static class StopCompletionIdentity
{
  public static string Revise(
    string original,
    IEnumerable<(Guid Id, long Revision)> stops
  )
  {
    var revisions = stops
      .Where(s => s.Revision != 0)
      .OrderBy(s => s.Id)
      .Select(s => new { s.Id, s.Revision })
      .ToArray();
    return revisions.Length == 0
      ? original
      : Convert.ToHexString(
        SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(new { original, revisions })
        )
      );
  }

  public static string Create(
    Guid id,
    int sequence,
    string job,
    string address,
    string city,
    string province,
    string country,
    string name,
    Guid? truckId,
    DateOnly? date,
    TimeOnly? time
  ) =>
    Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(
          new
          {
            id,
            sequence,
            job,
            address,
            city,
            province,
            country,
            name,
            truckId,
            date,
            time,
          }
        )
      )
    );
}
