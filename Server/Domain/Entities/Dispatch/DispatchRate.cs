namespace Domain.Entities.Dispatch;

public sealed class DispatchRate : BaseEntity
{
  public Guid DispatchId { get; set; }
  public decimal? Price { get; set; }
  public string Currency { get; set; } = "";
  public decimal? LoadedMiles { get; set; }
  public decimal? EmptyMiles { get; set; }
  public string ConnectionHash { get; set; } = "";
  public decimal? LoadedRatePerMile { get; set; }
  public decimal? TotalRatePerMile { get; set; }
  public DateTime CalculatedAt { get; set; }
}
