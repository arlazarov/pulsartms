namespace Application.Features.Routing.Models;

public sealed record ResolvedAddress(
  RoutePoint Point,
  string Address,
  string City,
  string Province,
  string Country,
  string ZipCode
);
