namespace Application.Caching;

public static class CacheBudgets
{
  private const long MiB = 1024 * 1024;
  public const long Reads = 16 * MiB;
  public const long RouteDisplay = 16 * MiB;
  public const long RouteIndexes = 32 * MiB;
  public const long Fuel = 8 * MiB;
  public const long TruckHistory = 8 * MiB;
}
