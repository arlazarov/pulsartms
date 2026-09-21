import type { MapPoint, RouteLeg, RoutePoint } from '../contracts.d.ts';

// A route as one path with the miles under each point, and the index where
// each leg ends.
export function routeGeometry(legs: RouteLeg[]): {
  path: MapPoint[];
  cumulative: number[];
  anchors: number[];
} {
  const path: MapPoint[] = [];
  const cumulative: number[] = [],
    anchors: number[] = [];
  let miles = 0;
  for (const leg of legs) {
    const lengths = leg.points
      .slice(1)
      .map((p, i) => angularDistance(leg.points[i], p));
    const total = lengths.reduce((a, b) => a + b, 0);
    leg.points.forEach((p, i) => {
      // Provider mileage, rather than geodesic mileage, defines route progress.
      if (i > 0) miles += total ? (lengths[i - 1] / total) * leg.miles : 0;
      if (i === 0 && path.length) return;
      path.push({ lat: p.latitude, lng: p.longitude });
      cumulative.push(miles);
    });
    if (path.length) anchors.push(path.length - 1);
  }
  return { path, cumulative, anchors };
}

function angularDistance(a: RoutePoint, b: RoutePoint): number {
  const r = Math.PI / 180;
  const dlat = (b.latitude - a.latitude) * r;
  const dlng = (b.longitude - a.longitude) * r;
  return (
    2 *
    Math.asin(
      Math.sqrt(
        Math.min(
          1,
          Math.sin(dlat / 2) ** 2 +
            Math.cos(a.latitude * r) *
              Math.cos(b.latitude * r) *
              Math.sin(dlng / 2) ** 2,
        ),
      ),
    )
  );
}
