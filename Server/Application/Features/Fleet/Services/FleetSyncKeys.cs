using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Services;

// The process-wide memory keys the fleet and load imports keep between
// runs. Each names the carrier: the imports run once per carrier, and one
// carrier's driver ids or "nothing changed" signature must never be read
// in another's run.
public static class FleetSyncKeys
{
  public static string DriverIds(Guid? company) =>
    $"fleet-driver-ids:{company:N}";

  public static string AssignmentSignature(Guid? company) =>
    $"assignment-sync-signature:{company:N}";

  public static string DispatchSnapshot(Guid? company, string provider) =>
    $"dispatch-sync-signature:{company:N}:{provider}";

  // After the fleet changes the driver list and the assignment signature
  // are read again. The load import's snapshot needs nothing here: it is
  // reused only while the fleet catalog's read generation is unchanged,
  // and every fleet change invalidates that group.
  public static void Forget(IMemoryCache cache, Guid? company)
  {
    cache.Remove(DriverIds(company));
    cache.Remove(AssignmentSignature(company));
  }
}
