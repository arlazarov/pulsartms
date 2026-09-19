using System.Security.Cryptography;
using System.Text.Json;

namespace Application.Features.Routing.Models;

public sealed record NextLoadLabels(Guid Id, IReadOnlyList<string> Names)
{
  public Guid? ExecutionLegId { get; init; }
}

public sealed record NextLoadRoutesResponse(
  string Revision,
  bool Unchanged,
  IReadOnlyList<NextLoadRoute>? Routes,
  IReadOnlyList<NextLoadLabels>? Labels = null
)
{
  public static string MetadataRevision(
    string geometryRevision,
    IReadOnlyList<NextLoadLabels> labels
  ) =>
    geometryRevision
    + "."
    + Convert.ToHexString(
      SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(labels))
    );

  public static bool HasGeometry(
    string? knownRevision,
    string geometryRevision
  ) => knownRevision?.Split('.')[0] == geometryRevision;

  public static NextLoadRoutesResponse Create(
    Guid truckId,
    Guid? currentId,
    IReadOnlyList<NextLoadRoute> routes,
    string? knownRevision
  )
  {
    var revision = Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(
          new
          {
            truckId,
            currentId,
            routes,
          }
        )
      )
    );
    return new(
      revision,
      revision == knownRevision,
      revision == knownRevision ? null : routes
    );
  }
}
