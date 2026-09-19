namespace Domain.Entities.Dispatch;

public sealed class DispatchWorkspace : BaseEntity
{
  public long Revision { get; set; }
  public string MetadataJson { get; set; } = "{}";
  public string StopExtrasJson { get; set; } = "{}";
  public bool OwnsStops { get; set; }
  public bool OwnsCommercial { get; set; }
  public string SourceStopsJson { get; set; } = "[]";
  public string SourceCommercialJson { get; set; } = "{}";
  public string? SourceReviewReason { get; set; }
  public DateTime RecordedAt { get; set; }
  public Guid RecordedBy { get; set; }
}

public sealed class DispatchWorkspaceRevision : BaseEntity
{
  public Guid DispatchId { get; set; }
  public long Revision { get; set; }
  public Guid IdempotencyKey { get; set; }
  public string RequestHash { get; set; } = "";
  public string SnapshotJson { get; set; } = "{}";
  public string BeforeJson { get; set; } = "{}";
  public string Summary { get; set; } = "";
  public DateTime RecordedAt { get; set; }
  public Guid RecordedBy { get; set; }
  public string ActorName { get; set; } = "";
}
