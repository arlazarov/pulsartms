using Client.Shared.Trucks;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Fuel.FuelReading;

public partial class FuelReading
{
    [Parameter] public double? Percent { get; set; }
    [Parameter] public string Variant { get; set; } = "pill";
    private bool IsMetric => string.Equals(Variant, "metric", StringComparison.OrdinalIgnoreCase);
    private string Tone => TelemetryTone.Fuel(Percent);
    private string Value => Percent is { } value ? $"{value:N0}%" : "—";
}
