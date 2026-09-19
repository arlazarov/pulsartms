using Client.Models.DTO.Dispatch.Workspace;
using Client.Models.DTO;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchStopFields
{
  [Parameter, EditorRequired]
  public DispatchWorkspaceStop Stop { get; set; } = default!;

  [Parameter, EditorRequired]
  public DispatchStopClockDraft Clock { get; set; } = default!;

  [Parameter]
  public bool Disabled { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }

  [Parameter]
  public EventCallback<Guid> VerifyAddressRequested { get; set; }

  [Parameter]
  public Func<string, CancellationToken, Task<RequestResponseDTO<VerifiedDispatchAddress>>>? AddressLookup { get; set; }

  private async Task ApplyAddressAsync(VerifiedDispatchAddress address)
  {
    if (Disabled) return;
    Stop.Address = address.Address;
    Stop.City = address.City;
    Stop.Province = address.Province;
    Stop.Country = address.Country;
    Stop.ZipCode = address.ZipCode;
    Stop.Latitude = address.Latitude;
    Stop.Longitude = address.Longitude;
    await NotifyAsync();
  }

  private string Id => $"stop-{Stop.Id}";
  private string? _clockError;
  private bool Cargo => DispatchWorkspaceStopDisplay.HasCargo(Stop);
  private string ReferenceLabel =>
    DispatchWorkspaceStopDisplay.ReferenceLabel(Stop);

  private Task NotifyAsync() => Changed.InvokeAsync();

  private async Task LocationChangedAsync()
  {
    Stop.Latitude = null;
    Stop.Longitude = null;
    await Changed.InvokeAsync();
  }

  private async Task TimeChangedAsync()
  {
    Clock.Apply(Stop, out _clockError);
    await Changed.InvokeAsync();
  }

  private async Task ModeChangedAsync()
  {
    if (Stop.AppointmentMode != "window")
    {
      Stop.ScheduledDate2 = null;
      Stop.ScheduledTime2 = null;
      Clock.End = "";
    }
    if (Stop.AppointmentMode == "unscheduled")
    {
      Stop.ScheduledDate = null;
      Stop.ScheduledTime = null;
      Clock.Start = "";
    }
    await TimeChangedAsync();
  }
}
