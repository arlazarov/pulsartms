using System.Collections.Immutable;
using Application.Features.Eta.Interfaces;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;

namespace Application.Features.Eta.Algorithms;

public readonly record struct EtaRouteTimingSegment(
  double StartMiles,
  double EndMiles,
  string Country,
  bool NorthOf60
)
{
  public bool IsSupported => Country.Length > 0 && !NorthOf60;
}

public sealed class EtaRouteTimingLeg
{
  public double StartMiles { get; }
  public double EndMiles => StartMiles + Miles;
  public double Miles { get; }
  public double Seconds { get; }
  public ImmutableArray<EtaRouteTimingSegment> Segments { get; }
  private readonly int pointCount;
  private readonly RoutePoint? first;
  private readonly RoutePoint? last;

  internal EtaRouteTimingLeg(
    double startMiles,
    RouteLeg leg,
    ImmutableArray<EtaRouteTimingSegment> segments
  )
  {
    StartMiles = startMiles;
    Miles = leg.Miles;
    Seconds = leg.Seconds;
    Segments = segments;
    pointCount = leg.Points.Count;
    first = leg.Points.FirstOrDefault();
    last = leg.Points.LastOrDefault();
  }

  internal bool Matches(RouteLeg leg) =>
    Miles.Equals(leg.Miles)
    && Seconds.Equals(leg.Seconds)
    && pointCount == leg.Points.Count
    && first == leg.Points.FirstOrDefault()
    && last == leg.Points.LastOrDefault();
}

public sealed class EtaRouteTiming
{
  public const int MaximumPreviewSamples = 5000;
  public ImmutableArray<EtaRouteTimingLeg> Legs { get; }
  public bool HasCompleteTravelTimes { get; }
  public int RetainedUnits { get; }
  private readonly double routeMiles;
  private readonly double routeSeconds;

  private EtaRouteTiming(
    TruckRoute route,
    ImmutableArray<EtaRouteTimingLeg> legs,
    bool complete
  )
  {
    routeMiles = route.Miles;
    routeSeconds = route.Seconds;
    Legs = legs;
    HasCompleteTravelTimes = complete;
    RetainedUnits = legs.Sum(leg => 1 + leg.Segments.Length);
  }

  internal bool Matches(TruckRoute route)
  {
    if (
      !routeMiles.Equals(route.Miles)
      || !routeSeconds.Equals(route.Seconds)
      || Legs.Length != route.Legs.Count
    )
      return false;
    for (var i = 0; i < Legs.Length; i++)
      if (!Legs[i].Matches(route.Legs[i]))
        return false;
    return true;
  }

  public static EtaRouteTiming? CompilePreview(
    TruckRoute route,
    IRouteRegionLookup regions,
    CancellationToken cancellationToken = default
  )
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (
      route.Legs.Count is 0 or > 40
      || !double.IsFinite(route.Miles)
      || route.Miles < 0
      || !double.IsFinite(route.Seconds)
      || route.Seconds < 0
    )
      return null;
    long points = 0;
    double samples = 0,
      miles = 0,
      seconds = 0;
    foreach (var leg in route.Legs)
    {
      cancellationToken.ThrowIfCancellationRequested();
      points += leg.Points.Count;
      if (
        points > 100_000
        || leg.Points.Count < 2
        || !double.IsFinite(leg.Miles)
        || leg.Miles < 0
        || !double.IsFinite(leg.Seconds)
        || leg.Seconds < 0
        || (leg.Miles == 0) != (leg.Seconds == 0)
      )
        return null;
      samples += Math.Ceiling(leg.Miles / 2);
      if (samples > MaximumPreviewSamples)
        return null;
      miles += leg.Miles;
      seconds += leg.Seconds;
    }
    if (
      Math.Abs(miles - route.Miles) > .01
      || Math.Abs(seconds - route.Seconds) > 1
    )
      return null;
    var legs = ImmutableArray.CreateBuilder<EtaRouteTimingLeg>(
      route.Legs.Count
    );
    double offset = 0;
    foreach (var leg in route.Legs)
    {
      var lengths = new double[leg.Points.Count - 1];
      double total = 0;
      if (!leg.Points[0].IsValid)
        return null;
      for (var i = 0; i < lengths.Length; i++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (!leg.Points[i + 1].IsValid)
          return null;
        total += lengths[i] = RouteGeometry.Distance(
          leg.Points[i],
          leg.Points[i + 1]
        );
      }
      if (!double.IsFinite(total) || (leg.Miles == 0 ? total != 0 : total <= 0))
        return null;
      var segments = new List<EtaRouteTimingSegment>();
      var count = (int)Math.Ceiling(leg.Miles / 2);
      var index = 0;
      double segmentStart = 0;
      for (var sample = 0; sample < count; sample++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var distance = total * (sample + .5) / count;
        while (
          index < lengths.Length - 1 && segmentStart + lengths[index] < distance
        )
          segmentStart += lengths[index++];
        var from = leg.Points[index];
        var to = leg.Points[index + 1];
        var fraction =
          lengths[index] > 0
            ? Math.Clamp((distance - segmentStart) / lengths[index], 0, 1)
            : 0;
        var region = regions.Find(
          new(
            from.Latitude + fraction * (to.Latitude - from.Latitude),
            from.Longitude + fraction * (to.Longitude - from.Longitude)
          )
        );
        var start = offset + leg.Miles * sample / count;
        var end = offset + leg.Miles * (sample + 1) / count;
        // Preview regions use uniform road-mile sampling, independent of
        // provider vertex density.
        if (
          segments.Count > 0
          && segments[^1].Country == region.Country
          && segments[^1].NorthOf60 == region.NorthOf60
        )
          segments[^1] = segments[^1] with { EndMiles = end };
        else
          segments.Add(new(start, end, region.Country, region.NorthOf60));
      }
      legs.Add(new(offset, leg, segments.ToImmutableArray()));
      offset += leg.Miles;
    }
    cancellationToken.ThrowIfCancellationRequested();
    return new(route, legs.MoveToImmutable(), true);
  }

  public static EtaRouteTiming Compile(
    TruckRoute route,
    IRouteRegionLookup regions
  )
  {
    var legs = ImmutableArray.CreateBuilder<EtaRouteTimingLeg>(
      route.Legs.Count
    );
    var complete = route.Legs.Count > 0;
    double offset = 0;
    foreach (var leg in route.Legs)
    {
      var segments = new List<EtaRouteTimingSegment>();
      var stationary =
        leg.Miles == 0
        && leg.Seconds == 0
        && leg.Points.Count >= 2
        && leg.Points.All(point => point == leg.Points[0]);
      var valid =
        leg.Points.Count >= 2
          && double.IsFinite(leg.Miles)
          && leg.Miles > 0
          && double.IsFinite(leg.Seconds)
          && leg.Seconds > 0
        || stationary;
      complete &= valid;
      if (valid && !stationary)
      {
        double total = 0;
        for (var i = 1; i < leg.Points.Count; i++)
          total += RouteGeometry.Distance(leg.Points[i - 1], leg.Points[i]);
        if (!double.IsFinite(total) || total <= 0)
          complete = false;
        else
        {
          double cursor = offset;
          for (var i = 1; i < leg.Points.Count; i++)
          {
            var from = leg.Points[i - 1];
            var to = leg.Points[i];
            var miles = RouteGeometry.Distance(from, to) / total * leg.Miles;
            var end = cursor + miles;
            var parts = Math.Max(1, (int)Math.Ceiling(miles / 2));
            for (var n = 0; n < parts && miles > 0; n++)
            {
              var a = cursor + miles * n / parts;
              var b = cursor + miles * (n + 1) / parts;
              var fraction = (n + .5) / parts;
              var region = regions.Find(
                new(
                  from.Latitude + fraction * (to.Latitude - from.Latitude),
                  from.Longitude + fraction * (to.Longitude - from.Longitude)
                )
              );
              // Keep the original two-mile regional sampling, then retain only
              // country transitions.
              if (
                segments.Count > 0
                && segments[^1].Country == region.Country
                && segments[^1].NorthOf60 == region.NorthOf60
              )
                segments[^1] = segments[^1] with { EndMiles = b };
              else
                segments.Add(new(a, b, region.Country, region.NorthOf60));
            }
            cursor = end;
          }
          if (segments.Count > 0)
            segments[^1] = segments[^1] with { EndMiles = offset + leg.Miles };
        }
      }
      legs.Add(new(offset, leg, segments.ToImmutableArray()));
      offset += leg.Miles;
    }
    return new(route, legs.MoveToImmutable(), complete);
  }
}
