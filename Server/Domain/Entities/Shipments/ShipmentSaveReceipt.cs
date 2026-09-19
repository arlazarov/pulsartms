namespace Domain.Entities.Shipments;

public sealed class ShipmentSaveReceipt : BaseEntity
{
  public Guid ActorId { get; set; }
  public Guid AggregateId { get; set; }
  public string RequestHash { get; set; } = "";
  public string ResponseJson { get; set; } = "";
  public DateTime RecordedAt { get; set; }
}
