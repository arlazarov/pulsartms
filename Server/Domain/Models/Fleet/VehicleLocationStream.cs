namespace Domain.Models.Fleet;

public class VehicleLocationStream
{
  public IReadOnlyList<VehicleLocationPoint> Data { get; set; } = [];
  public string EndCursor { get; set; } = string.Empty;
  public bool HasNextPage { get; set; }
}
