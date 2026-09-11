namespace Application.Features.Fuel.Models;

public sealed class FuelStationLookupState
{
  public string Signature { get; set; } = "";
  public string Revision { get; set; } = "";
  public DateTime NextAttemptAt { get; set; }
  public int Failures { get; set; }
  public bool Pending { get; set; }
  public string? ErrorCode { get; set; }
  public PlaceSearchResult? Result { get; set; }
}

public sealed record FuelStationLookupResult(string Revision, PlaceSearchResult? Place);
