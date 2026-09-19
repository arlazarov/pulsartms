using Client.Models.DTO.Dispatch.Workspace;

namespace Client.Pages.Dispatch;

public sealed record DispatchStopDraft(
  StopCorrectionRequest Request,
  string DriverName,
  string CoDriverName,
  bool CanSubmit,
  Guid? InitialDriverId,
  Guid? InitialCoDriverId,
  string InitialStatus
);

public sealed class DispatchStopDrafts
{
  public Dictionary<Guid, DispatchStopDraft> Entries { get; } = [];
  private readonly List<Guid> _order = [];
  public IEnumerable<KeyValuePair<Guid, DispatchStopDraft>> Ordered =>
    _order
      .Where(Entries.ContainsKey)
      .Select(id => KeyValuePair.Create(id, Entries[id]));
  public Guid? PendingStopId { get; set; }
  public bool HasChanges => Entries.Count > 0;
  public bool CanSubmit => Entries.Values.All(x => x.CanSubmit);

  public void Set(Guid stopId, DispatchStopDraft? draft)
  {
    Entries.Remove(stopId);
    _order.Remove(stopId);
    if (draft is not null)
    {
      Entries.Add(stopId, draft);
      _order.Add(stopId);
    }
  }

  public void Clear()
  {
    Entries.Clear();
    _order.Clear();
    PendingStopId = null;
  }

  public bool IsCompleted(
    DispatchWorkspaceStop stop,
    List<DispatchWorkspaceStop> stops,
    bool saved
  )
  {
    var index = stops.IndexOf(stop);
    foreach (var (id, draft) in Ordered)
    {
      if (
        draft.Request.Completion == "completed"
        && stop.Transfer is null
        && index <= stops.FindIndex(x => x.Id == id)
      )
        saved = true;
      else if (id == stop.Id && draft.Request.Completion == "pending")
        saved = false;
    }
    return saved;
  }

  public (
    Guid? DriverId,
    Guid? CoDriverId,
    string Driver,
    string CoDriver
  ) Names(DispatchWorkspaceStop stop, List<DispatchWorkspaceStop> stops)
  {
    var driverId = stop.DriverId;
    var coDriverId = stop.CoDriverId;
    var driver = stop.DriverName ?? "";
    var coDriver = stop.CoDriverName ?? "";
    foreach (var (id, draft) in Ordered)
    {
      var request = draft.Request;
      var selected = stops.FirstOrDefault(x => x.Id == id);
      if (selected is null)
        continue;
      var from = stops.FindIndex(x =>
        x.Id == (request.AssignmentScope == "onward" ? id : request.FromStopId)
      );
      var to =
        request.AssignmentScope == "onward"
          ? stops.Count - 1
          : stops.FindIndex(x => x.Id == request.ToStopId);
      var applies = request.AssignmentScope switch
      {
        "stop" => stop.Id == id,
        "current" => stop.ExecutionLegId == selected.ExecutionLegId,
        "all" => true,
        "range" or "onward" => from >= 0
          && to >= from
          && stops.IndexOf(stop) >= from
          && stops.IndexOf(stop) <= to,
        _ => false,
      };
      if (!applies)
        continue;
      if (request.ChangeDriver == true)
      {
        driverId = request.DriverId;
        driver = draft.DriverName;
      }
      if (request.ChangeCoDriver == true)
      {
        coDriverId = request.CoDriverId;
        coDriver = draft.CoDriverName;
      }
    }
    return (driverId, coDriverId, driver, coDriver);
  }
}
