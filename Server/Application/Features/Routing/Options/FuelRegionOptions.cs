using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Application.Features.Routing.Options;

public sealed class FuelRegionOptions
{
  [Range(20, 100)]
  public double CellMiles { get; set; } = 50;

  [Range(.01, 2)]
  public double ExpensivePremiumUsdPerGallon { get; set; } = .15;

  [Range(1, 10)]
  public int MinimumStations { get; set; } = 2;

  [Range(25, 150)]
  public double LocalSearchMiles { get; set; } = 75;

  [Range(100, 500)]
  public double EscapeSearchMiles { get; set; } = 250;

  [Range(25, 300)]
  public double AfterDeliveryBufferMiles { get; set; } = 75;

  [Range(50, 500)]
  public double PoorAreaBufferMiles { get; set; } = 200;

  [Range(1, 5)]
  public int MaximumRoadChecks { get; set; } = 3;

  [Range(2, 60)]
  public double CandidateSearchMiles { get; set; } = 30;

  [Range(30, 100)]
  public double ZoneSearchMiles { get; set; } = 60;

  [Range(1, 4)]
  public int ZoneRoadChecks { get; set; } = 3;

  [Range(8, 32)]
  public int CandidateShortlistLimit { get; set; } = 24;

  [Range(2, 20)]
  public int CandidateRoadChecks { get; set; } = 12;

  // Retained in saved policy signatures; checked detours are now ranked by
  // total cost.
  [Range(5, 100)]
  public double MaximumExtraMiles { get; set; } = 40;
  public string Signature =>
    JsonSerializer.Serialize(
      new
      {
        CellMiles,
        ExpensivePremiumUsdPerGallon,
        MinimumStations,
        LocalSearchMiles,
        EscapeSearchMiles,
        AfterDeliveryBufferMiles,
        PoorAreaBufferMiles,
        MaximumRoadChecks,
        CandidateSearchMiles,
        ZoneSearchMiles,
        ZoneRoadChecks,
        CandidateShortlistLimit,
        CandidateRoadChecks,
        MaximumExtraMiles,
      }
    );
}
