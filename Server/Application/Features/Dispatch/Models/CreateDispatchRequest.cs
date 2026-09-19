namespace Application.Features.Dispatch.Models;

public sealed class CreateDispatchRequest
{
  public Guid IdempotencyKey { get; set; }
  public string OrderNumber { get; set; } = "";
  public string CustomerName { get; set; } = "";
  public decimal? Price { get; set; }
  public string Currency { get; set; } = "";
  public List<DispatchWorkspaceStop> Stops { get; set; } = [];
}
