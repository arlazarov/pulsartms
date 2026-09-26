using Application.Interfaces;

namespace Server.Tests.Support;

public sealed record TestFleetCollectionState(
  bool Enabled = false,
  bool Active = false,
  bool HighFrequencyLocations = true
) : IFleetCollectionState;
