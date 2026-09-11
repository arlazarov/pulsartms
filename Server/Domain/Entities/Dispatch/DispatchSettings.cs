namespace Domain.Entities.Dispatch;

public sealed class DispatchSettings : BaseEntity
{
  public static readonly Guid SingletonId = new("731f4d30-c85a-4af0-a14a-29db18bd4a47");
  public string LoadNumberPrefix { get; set; } = string.Empty;
  public long Revision { get; set; }
  public DateTime UpdatedAt { get; set; }
}
