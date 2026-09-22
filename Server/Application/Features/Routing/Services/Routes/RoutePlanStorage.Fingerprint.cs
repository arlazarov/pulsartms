using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Domain.Models.Routing;

namespace Application.Features.Routing.Services.Routes;

public static partial class RoutePlanStorage
{
  private sealed record Captured(Guid Id, string Manifest, byte[] Fingerprint);

  private static readonly ConditionalWeakTable<RoutePlan, Captured> Captures =
    new();

  private static byte[] Fingerprint(RoutePlan plan)
  {
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    Append(plan.Route);
    Append(plan.ReferenceRoute);
    return hash.GetHashAndReset();

    void Append(TruckRoute? route)
    {
      Span<byte> buffer = stackalloc byte[1024];
      BinaryPrimitives.WriteInt64LittleEndian(buffer, route?.Legs.Count ?? -1);
      hash.AppendData(buffer[..8]);
      if (route is null)
        return;
      foreach (var leg in route.Legs)
      {
        BinaryPrimitives.WriteInt64LittleEndian(buffer, leg.Points.Count);
        hash.AppendData(buffer[..8]);
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, leg.Miles);
        BinaryPrimitives.WriteDoubleLittleEndian(buffer[8..], leg.Seconds);
        hash.AppendData(buffer[..16]);
        var length = 0;
        foreach (var point in leg.Points)
        {
          BinaryPrimitives.WriteDoubleLittleEndian(
            buffer[length..],
            point.Latitude
          );
          BinaryPrimitives.WriteDoubleLittleEndian(
            buffer[(length + 8)..],
            point.Longitude
          );
          length += 16;
          if (length == buffer.Length)
          {
            hash.AppendData(buffer);
            length = 0;
          }
        }
        hash.AppendData(buffer[..length]);
      }
    }
  }
}
