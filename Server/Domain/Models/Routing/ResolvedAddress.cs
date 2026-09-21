namespace Domain.Models.Routing;

public sealed record ResolvedAddress(
  RoutePoint Point,
  string Address,
  string City,
  string Province,
  string Country,
  string ZipCode
);
