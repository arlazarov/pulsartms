using Client.Models.DTO.Planning;
namespace Client.Models.DTO.Dispatch;

public class TruckDispatchBoardResponse
{
  public DriverHosClocks? Hos { get; set; }
  public Client.Models.DTO.Planning.DriverCycleSnapshot? CurrentCycle { get; set; }
  public string Key { get; set; } = string.Empty;
  public Guid? TruckId { get; set; }
  public string TruckNumber { get; set; } = string.Empty;
  public string DriverName { get; set; } = string.Empty;
  public string TrailerNumber { get; set; } = string.Empty;
  public decimal Speed { get; set; }
  public string EngineState { get; set; } = string.Empty;
  public List<DispatchResponse> Dispatches { get; set; } = [];
}
