namespace Domain.Entities.Dispatch;

public sealed class DispatchSettings : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public static readonly Guid SingletonId = new(
    "731f4d30-c85a-4af0-a14a-29db18bd4a47"
  );
  public string LoadNumberPrefix { get; set; } = string.Empty;
  public string TemperatureUnit { get; set; } = "both";
  public string DistanceUnit { get; set; } = "both";

  // Fuel plans are prepared in the background either way. This decides
  // only whether a prepared plan may be sent without a dispatcher pressing
  // Send plan. Off unless a company turns it on; a company with no settings
  // row reads it as off too.
  public bool AutomaticFuelSending { get; set; }
  public long Revision { get; set; }
  public DateTime UpdatedAt { get; set; }
}
