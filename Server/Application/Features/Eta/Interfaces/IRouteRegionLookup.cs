using Application.Features.Routing.Models;
namespace Application.Features.Eta.Interfaces;
public record RouteRegion(string Country, string TimeZoneId, bool NorthOf60);
public interface IRouteRegionLookup { RouteRegion Find(RoutePoint point); }
