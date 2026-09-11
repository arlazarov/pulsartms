using System.ComponentModel.DataAnnotations;

namespace Application.Features.Routing.Options;

public sealed class RoutePreparationOptions
{
  [Range(10, 300)] public int TickSeconds { get; set; } = 60;
  [Range(1, 30)] public int BatchSize { get; set; } = 10;
  [Range(10, 500)] public int ScanPageSize { get; set; } = 100;
  [Range(1, 60)] public int HorizonDays { get; set; } = 14;
  [Range(1, 14)] public int PrewarmDays { get; set; } = 3;
  [Range(5, 1440)] public int RepairMinutes { get; set; } = 360;
  [Range(16, 2048)] public int PendingCapacity { get; set; } = 512;
  [Range(128, 8192)] public int StateCapacity { get; set; } = 2048;
  [Range(30, 1800)] public int RetrySeconds { get; set; } = 300;
}
