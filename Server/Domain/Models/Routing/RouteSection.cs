namespace Domain.Models.Routing;

public sealed record RouteSection(
  bool Restricted,
  bool Unsupported,
  bool Truck,
  int? Start,
  int? End
)
{
  // A crossing by ferry, which the provider reports as its own section.
  public bool Ferry { get; init; }
}
