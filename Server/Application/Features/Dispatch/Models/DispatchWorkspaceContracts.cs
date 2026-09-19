namespace Application.Features.Dispatch.Models;

public sealed class DispatchWorkspaceMetadata
{
  public string OrderNumber { get; set; } = "";
  public string CustomerName { get; set; } = "";
  public decimal? Price { get; set; }
  public string Currency { get; set; } = "";
  public string BrokerCompany { get; set; } = "";
  public string BrokerContact { get; set; } = "";
  public string BrokerPhone { get; set; } = "";
  public string BrokerEmail { get; set; } = "";
  public string BrokerReference { get; set; } = "";
  public string BillTo { get; set; } = "";
  public string LoadInstructions { get; set; } = "";
  public Guid? BrokerId { get; set; }
  public BrokerPaymentTerms PaymentTerms { get; set; } = new();
  public List<LoadAdjustment> Adjustments { get; set; } = [];
}

public sealed class DispatchWorkspaceStop
{
  public bool CanCorrect { get; set; }
  public Guid? TruckId { get; set; }
  public Guid? TrailerId { get; set; }
  public Guid? DriverId { get; set; }
  public Guid? CoDriverId { get; set; }
  public Guid Id { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public DispatchWorkspaceTransfer? Transfer { get; set; }
  public int Sequence { get; set; }
  public string SegmentKey { get; set; } = "";
  public bool IsNew { get; set; }
  public bool CanEdit { get; set; }
  public bool CanMove { get; set; }
  public bool CanRemove { get; set; }
  public string? LockReason { get; set; }
  public string Job { get; set; } = "";
  public string Name { get; set; } = "";
  public string Address { get; set; } = "";
  public string City { get; set; } = "";
  public string Province { get; set; } = "";
  public string Country { get; set; } = "";
  public string ZipCode { get; set; } = "";
  public decimal? Latitude { get; set; }
  public decimal? Longitude { get; set; }
  public string StopNo { get; set; } = "";
  public string AppointmentReference { get; set; } = "";
  public string ContactName { get; set; } = "";
  public string ContactPhone { get; set; } = "";
  public string ContactEmail { get; set; } = "";
  public string Notes { get; set; } = "";
  public string Commodity { get; set; } = "";
  public decimal? Weight { get; set; }
  public string WeightUnit { get; set; } = "";
  public decimal? Pieces { get; set; }
  public decimal? Pallets { get; set; }
  public string Temperature { get; set; } = "";
  public string TemperatureUnit { get; set; } = "";
  public string AppointmentMode { get; set; } = "unscheduled";
  public string TimeZoneId { get; set; } = "";
  public DateOnly? ScheduledDate { get; set; }
  public TimeOnly? ScheduledTime { get; set; }
  public DateOnly? ScheduledDate2 { get; set; }
  public TimeOnly? ScheduledTime2 { get; set; }
  public string TruckNumber { get; set; } = "";
  public string TrailerNumber { get; set; } = "";
  public string DriverName { get; set; } = "";
  public string CoDriverName { get; set; } = "";
}

public sealed class DispatchWorkspaceResponse
{
  public LoadBillingTotals? BillingTotals { get; set; }
  public DispatchResponse Load { get; set; } = new();
  public long Revision { get; set; }
  public string SourceFingerprint { get; set; } = "";
  public DispatchWorkspaceMetadata Metadata { get; set; } = new();
  public List<DispatchWorkspaceStop> Stops { get; set; } = [];
  public bool CanEdit { get; set; }
  public string? ReadOnlyReason { get; set; }
  public bool LocallyManaged { get; set; }
  public string SourceName { get; set; } = "";
  public DateTime? SourceUpdatedAt { get; set; }
  public string? SourceReviewReason { get; set; }
  public DispatchAssignmentProposal? SourceAssignment { get; set; }
  public IReadOnlyList<DispatchAcceptedAssignment> AcceptedAssignments { get; set; } =
    [];
  public List<DispatchWorkspaceHistory> History { get; set; } = [];
}

public sealed class DispatchWorkspaceTransfer
{
  public Guid SwitchId { get; set; }
  public string Kind { get; set; } = "";
  public string Action { get; set; } = "";
  public string Status { get; set; } = "";
  public DateTime? ActualAt { get; set; }
  public string OutgoingTruckNumber { get; set; } = "";
  public string IncomingTruckNumber { get; set; } = "";
  public string OutgoingTrailerNumber { get; set; } = "";
  public string IncomingTrailerNumber { get; set; } = "";
  public string OutgoingDriverName { get; set; } = "";
  public string IncomingDriverName { get; set; } = "";
}

public sealed record DispatchWorkspaceHistory(
  long Revision,
  DateTime RecordedAt,
  string ActorName,
  string Summary
);

public sealed class UpdateDispatchWorkspaceRequest
{
  public long ExpectedRevision { get; set; }
  public string SourceFingerprint { get; set; } = "";
  public Guid IdempotencyKey { get; set; }
  public DispatchWorkspaceMetadata Metadata { get; set; } = new();
  public List<DispatchWorkspaceStop> Stops { get; set; } = [];
}

public sealed class VerifyDispatchAddressRequest
{
  public string Address { get; set; } = "";
  public string City { get; set; } = "";
  public string Province { get; set; } = "";
  public string Country { get; set; } = "";
  public string ZipCode { get; set; } = "";
}

public sealed record VerifiedDispatchAddress(
  string Address,
  string City,
  string Province,
  string Country,
  string ZipCode,
  decimal Latitude,
  decimal Longitude
);
