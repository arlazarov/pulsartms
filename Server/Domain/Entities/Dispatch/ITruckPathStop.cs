namespace Domain.Entities.Dispatch;

public interface ITruckPathStop
{
  Guid Id { get; }
  int Sequence { get; }
  Guid? TruckId { get; }
  string TruckNumber { get; }
  string Job { get; }
  string StateAfter { get; }
  string? ManualAction { get; }
  string? ManualStateAfter { get; }
}
