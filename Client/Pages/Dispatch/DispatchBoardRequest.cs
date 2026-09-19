namespace Client.Pages.Dispatch;

internal sealed record DispatchBoardRequest(
  int Page,
  string Search,
  Guid? TruckId,
  int View,
  DateOnly Date,
  bool Completed = false
)
{
  public string Url =>
    Completed
      ? $"api/dispatch?page={Page}&pageSize=12&status=completed&search={Uri.EscapeDataString(Search)}"
        + (TruckId.HasValue ? $"&truckId={TruckId}" : "")
      : $"api/dispatch/board?page={Page}&pageSize=12&search={Uri.EscapeDataString(Search)}&date={Date:yyyy-MM-dd}"
        + (TruckId.HasValue ? $"&truckId={TruckId}" : "")
        + "&includePlanned=true";
}
