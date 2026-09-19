namespace Domain.Entities.Mileage;

public sealed class MileageAllocationPolicy : BaseEntity
{
  public static readonly Guid SingletonId = new(
    "ed1209a7-3d79-41e5-b183-19d59f8b2419"
  );
  public long Revision { get; set; }
  public string YardReturn { get; set; } = "unallocated";
  public string Home { get; set; } = "unallocated";
  public string Maintenance { get; set; } = "unallocated";
  public string Reposition { get; set; } = "unallocated";
  public DateTime? UpdatedAt { get; set; }
  public Guid? UpdatedBy { get; set; }
}
