using Microsoft.AspNetCore.Components;
using Client.Models.DTO.Planning;

namespace Client.Shared.DriverStatus.DriverHours;

public partial class DriverHours
{
    [Parameter] public DriverHosClocks? Clocks { get; set; }
    [Parameter] public bool ShowDutyStatus { get; set; } = true;
    [Parameter] public Client.Models.DTO.Planning.DriverDutyStatus? Status { get; set; }
    private static string Fill(long? milliseconds, int hours) =>
        Math.Clamp((milliseconds ?? 0) / (hours * 3600000d) * 100, 0, 100)
            .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static string Format(long? milliseconds)
    {
        if (milliseconds is null) return "—";
        var minutes = Math.Max(0, milliseconds.Value) / 60000;
        return $"{minutes / 60}:{minutes % 60:00}";
    }
}
