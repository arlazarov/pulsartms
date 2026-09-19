using Client.Models.DTO.Execution;

namespace Client.Pages.Dispatch;

public sealed class SwitchLoadDraft
{
  public SwitchLoadOption Source { get; init; } = default!;
  public string TransferKind { get; set; } = "drop_hook";
  public string Boundary { get; set; } = "pair";
  public Guid? ReleaseId { get; set; }
  public Guid? ReceiveId { get; set; }
  public Guid? SplitId { get; set; }
  public Guid? OutTruck { get; set; }
  public Guid? OutDriver { get; set; }
  public Guid? OutCoDriver { get; set; }
  public Guid? Trailer { get; set; }
  public Guid? InTruck { get; set; }
  public Guid? InDriver { get; set; }
  public Guid? InCoDriver { get; set; }
  public MileageTimeDraft ReleaseTime { get; } = new();
  public MileageTimeDraft ReceiveTime { get; } = new();

  public static SwitchLoadDraft From(SwitchLoadOption source) =>
    new()
    {
      Source = source,
      OutTruck = source.Outgoing?.TruckId,
      OutDriver = source.Outgoing?.DriverId,
      OutCoDriver = source.Outgoing?.CoDriverId,
      Trailer = source.Outgoing?.TrailerId,
    };

  public SwitchLoadChange? Request(out string? error)
  {
    error = null;
    if (OutTruck is null || InTruck is null)
      error = "Select both outgoing and incoming trucks.";
    else if (TransferKind == "drop_hook" && Trailer is null)
      error = "Select the trailer carrying this load for Drop / Hook.";
    else if (Boundary == "pair" && (ReleaseId is null || ReceiveId is null))
      error = "Select the two exact source visits for release and receipt.";
    else if (Boundary == "after" && SplitId is null)
      error = "Choose the last visit before the new transfer.";
    if (error is not null)
      return null;
    if (
      !ReleaseTime.TryRead(out var release)
      || !ReceiveTime.TryRead(out var receive)
    )
    {
      error = "Enter valid planned dates and times, or leave them blank.";
      return null;
    }
    if (release > receive)
    {
      error = "Planned receipt cannot precede release.";
      return null;
    }
    return new(
      Source.DispatchId,
      Boundary == "pair" ? ReleaseId : null,
      Boundary == "pair" ? ReceiveId : null,
      Source.SourceSignature,
      new(OutTruck!.Value, OutDriver, Trailer, OutCoDriver),
      new(InTruck!.Value, InDriver, Trailer, InCoDriver)
    )
    {
      SplitAfterVisitId = Boundary == "after" ? SplitId : null,
      OutgoingLegId = Source.OutgoingLegId,
      ExpectedOutgoingRevision = Source.ExpectedOutgoingRevision,
      TransferKind = TransferKind,
      PlannedReleaseAt = release,
      PlannedReceiveAt = receive,
    };
  }
}
