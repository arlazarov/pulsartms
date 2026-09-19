using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchRig
{
  private readonly string _smokeGradientId = $"rig-smoke-{Guid.NewGuid():N}";

  [Parameter]
  public string TruckNumber { get; set; } = "";

  [Parameter]
  public string TrailerNumber { get; set; } = "";

  [Parameter]
  public decimal Speed { get; set; }

  [Parameter]
  public string EngineState { get; set; } = "";

  [Parameter]
  public DriverHosClocks? Hos { get; set; }
  private string MotionState =>
    DispatchRigStatus.Resolve(Speed, EngineState, Hos, DateTime.UtcNow);
  private bool Sleeping => MotionState == "sleeping";
  private string MotionLabel =>
    MotionState switch
    {
      "moving" => $"Moving · {Speed:0} mph",
      "sleeping" => "Sleeper Berth",
      "idling" => "Idle",
      "off" => "Engine off",
      _ => "Parked",
    };
}
