using Application.Features.Eta.Models;
using Application.Features.Fleet.Models;

namespace Application.Features.Dispatch.Models;

public class TruckDispatchBoardResponse
{
  public DriverHosClocks? Hos { get; set; }
  public DriverCycleSnapshot? CurrentCycle { get; set; }
  public string Key { get; set; } = string.Empty;
  public Guid? TruckId { get; set; }
  public string TruckNumber { get; set; } = string.Empty;
  public string DriverName { get; set; } = string.Empty;
  public string TrailerNumber { get; set; } = string.Empty;
  public List<DispatchResponse> Dispatches { get; set; } = [];
}
