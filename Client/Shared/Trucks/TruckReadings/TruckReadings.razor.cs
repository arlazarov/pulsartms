using Microsoft.AspNetCore.Components;

namespace Client.Shared.Trucks.TruckReadings;

public partial class TruckReadings
{
  // Null is a speed nobody knows, and reads as a dash, never as 0 mph.
  [Parameter]
  public decimal? Speed { get; set; }

  // The speed the engine's tone is read from, when the holder reads it
  // differently from the one it shows: Fleet shows a stale speed as unknown
  // but still calls an engine running on it. Defaults to the shown speed.
  [Parameter]
  public decimal? EngineSpeed { get; set; }

  [Parameter]
  public double? Fuel { get; set; }

  [Parameter]
  public string? Engine { get; set; }

  [Parameter]
  public RenderFragment? FuelNote { get; set; }

  [Parameter]
  public RenderFragment? ChildContent { get; set; }

  private string EngineTone =>
    TelemetryTone.Engine(EngineSpeed ?? Speed ?? 0, Engine);
}
