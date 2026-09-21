using System.Buffers;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.TomTom;

// Reading TomTom's answer into a route: its legs, their points and their
// times, refusing one with more geometry than is safe to hold.
public sealed partial class TomTomRoutingProvider
{
  private TruckRoute ParseRoute(
    JsonElement root,
    int expectedLegs,
    int index = 0
  )
  {
    try
    {
      if (!root.TryGetProperty("routes", out var routes))
        throw new RoutePlanningException(
          "TomTom returned an incomplete route response. Please retry shortly.",
          DateTime.UtcNow.AddMinutes(5)
        );
      if (routes.GetArrayLength() == 0)
        throw new RoutePlanningException(
          "TomTom could not find a truck route for these stops."
        );
      var source = routes[index];
      var summary = source.GetProperty("summary");
      var legs = source.GetProperty("legs");
      if (legs.GetArrayLength() != expectedLegs)
        throw new RoutePlanningException(
          "TomTom returned an incomplete route.",
          DateTime.UtcNow.AddMinutes(5)
        );
      long pointCount = 0;
      foreach (var leg in legs.EnumerateArray())
      {
        pointCount += leg.GetProperty("points").GetArrayLength();
        if (pointCount > MaximumRoutePoints)
          throw new RoutePlanningException(
            "TomTom returned more route geometry than can be processed safely. The saved plan has been kept.",
            DateTime.UtcNow.AddMinutes(5)
          );
      }
      var result = new TruckRoute
      {
        Miles = summary.GetProperty("lengthInMeters").GetDouble() / 1609.344,
        Seconds = summary.GetProperty("travelTimeInSeconds").GetDouble(),
      };
      if (
        !double.IsFinite(result.Miles)
        || result.Miles <= 0
        || !double.IsFinite(result.Seconds)
        || result.Seconds < 0
      )
        throw new RoutePlanningException(
          "TomTom returned invalid route metrics.",
          DateTime.UtcNow.AddMinutes(5)
        );
      foreach (var leg in legs.EnumerateArray())
      {
        var ls = leg.GetProperty("summary");
        var lp = leg.GetProperty("points")
          .EnumerateArray()
          .Select(x => new RoutePoint(
            x.GetProperty("latitude").GetDouble(),
            x.GetProperty("longitude").GetDouble()
          ))
          .ToList();
        if (lp.Count < 2 || lp.Any(x => !x.IsValid))
          throw new RoutePlanningException(
            "TomTom returned incomplete route geometry.",
            DateTime.UtcNow.AddMinutes(5)
          );
        var miles = ls.GetProperty("lengthInMeters").GetDouble() / 1609.344;
        var seconds = ls.GetProperty("travelTimeInSeconds").GetDouble();
        if (
          !double.IsFinite(miles)
          || miles < 0
          || !double.IsFinite(seconds)
          || seconds < 0
        )
          throw new RoutePlanningException(
            "TomTom returned invalid route metrics.",
            DateTime.UtcNow.AddMinutes(5)
          );
        result.Legs.Add(new(miles, seconds, lp));
        result.Points.AddRange(result.Points.Count == 0 ? lp : lp.Skip(1));
      }
      if (source.TryGetProperty("sections", out var sections))
        sectionsValidator.Validate(result, TomTomRouteSections.Read(sections));
      return CanonicalTiming(result);
    }
    catch (Exception ex)
      when (ex
          is KeyNotFoundException
            or InvalidOperationException
            or FormatException
            or OverflowException
      )
    {
      throw new RoutePlanningException(
        "TomTom returned an incomplete or invalid route response. Please retry shortly.",
        DateTime.UtcNow.AddMinutes(5)
      );
    }
  }

  private static TruckRoute CanonicalTiming(TruckRoute route)
  {
    if (!route.TryGetLegSeconds(out var seconds))
      throw new RoutePlanningException(
        "TomTom returned inconsistent route timing. The saved route has been kept.",
        DateTime.UtcNow.AddMinutes(5)
      );
    route.Seconds = seconds;
    return route;
  }
}
