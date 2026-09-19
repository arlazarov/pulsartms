namespace Infrastructure.Integrations.Samsara.Models;

public class SamsaraLocationSpeedStream
{
  public IReadOnlyList<SamsaraLocationSpeed> Data { get; set; } = [];
  public SamsaraStreamPagination Pagination { get; set; } = new();
}

public class SamsaraLocationSpeed
{
  public DateTime HappenedAtTime { get; set; }
  public SamsaraLocationSpeedAsset Asset { get; set; } = new();
  public SamsaraLocationSpeedLocation? Location { get; set; }
  public SamsaraLocationSpeedSpeed? Speed { get; set; }
  public SamsaraLocationSpeedAddress? Address { get; set; }
}

public class SamsaraLocationSpeedAsset
{
  public string Id { get; set; } = string.Empty;
}

public class SamsaraLocationSpeedLocation
{
  public SamsaraLocationSpeedAddress? Address { get; set; }
  public decimal Latitude { get; set; }
  public decimal Longitude { get; set; }
  public decimal HeadingDegrees { get; set; }
  public decimal AccuracyMeters { get; set; }
}

public class SamsaraLocationSpeedSpeed
{
  public decimal? GpsSpeedMetersPerSecond { get; set; }
  public decimal? EcuSpeedMetersPerSecond { get; set; }
}

public class SamsaraLocationSpeedAddress
{
  public string FormattedAddress { get; set; } = string.Empty;
  public string StreetNumber { get; set; } = string.Empty;
  public string Street { get; set; } = string.Empty;
  public string City { get; set; } = string.Empty;
  public string State { get; set; } = string.Empty;
  public string PostalCode { get; set; } = string.Empty;
  public string Country { get; set; } = string.Empty;
}

public class SamsaraStreamPagination
{
  public string EndCursor { get; set; } = string.Empty;
  public bool HasNextPage { get; set; }
}
