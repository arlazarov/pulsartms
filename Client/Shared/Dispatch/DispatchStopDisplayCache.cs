using Client.Models.DTO.Dispatch;

namespace Client.Shared.Dispatch;

public sealed class DispatchStopDisplayCache
{
  private Input[] inputs = [];

  private sealed record Input(
    DispatchStopResponse Stop,
    int Sequence,
    string Job,
    string Address,
    string City,
    string Province,
    string ZipCode,
    string Country
  )
  {
    public bool Matches(DispatchStopResponse stop) =>
      ReferenceEquals(Stop, stop)
      && Sequence == stop.Sequence
      && Job == stop.Job
      && Address == stop.Address
      && City == stop.City
      && Province == stop.Province
      && ZipCode == stop.ZipCode
      && Country == stop.Country;
  }

  public IReadOnlyList<DispatchStopResponse> OrderedStops
  {
    get;
    private set;
  } = [];
  public IReadOnlyList<DispatchStopVisit> Visits { get; private set; } = [];
  public string Summary { get; private set; } = "";

  public void Update(IReadOnlyList<DispatchStopResponse> stops)
  {
    var changed = inputs.Length != stops.Count;
    for (var index = 0; !changed && index < inputs.Length; index++)
      changed = !inputs[index].Matches(stops[index]);
    if (!changed)
      return;
    inputs = stops
      .Select(stop => new Input(
        stop,
        stop.Sequence,
        stop.Job,
        stop.Address,
        stop.City,
        stop.Province,
        stop.ZipCode,
        stop.Country
      ))
      .ToArray();
    OrderedStops = stops.OrderBy(stop => stop.Sequence).ToArray();
    Visits = DispatchStopPresentation.OrderedVisits(OrderedStops);
    Summary = DispatchStopPresentation.Summary(OrderedStops);
  }
}
