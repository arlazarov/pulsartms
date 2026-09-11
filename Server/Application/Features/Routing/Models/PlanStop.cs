namespace Application.Features.Routing.Models;

public sealed record PlanStop(Guid Id, string Name, string Address, int Sequence, RoutePoint Point)
{
  public string Job { get; init; } = "";
  public DateOnly? ScheduledDate { get; init; }
  public TimeOnly? ScheduledTime { get; init; }
  public DateOnly? ScheduledDate2 { get; init; }
  public TimeOnly? ScheduledTime2 { get; init; }
  public string Commodity { get; init; } = "";
  public string Notes { get; init; } = "";
}
