namespace Application.Interfaces;

// Fleet reads need to know how this process receives telemetry, but they do
// not own or orchestrate synchronization. The synchronization host supplies
// this small runtime contract so Fleet does not depend on that feature.
public interface IFleetCollectionState
{
  bool Enabled { get; }
  bool Active { get; }
  bool HighFrequencyLocations { get; }
}
