import type { MapPlan, MapPoint } from '../contracts.d.ts';
import { routeGeometry } from '../geometry/routeGeometry.ts';
import { routePosition } from '../geometry/routePosition.ts';
import { emptyRouteLegs } from './emptyRouteLegs.ts';

// The shape of a planned route, with nothing drawn: the points it runs
// through, the miles at each of them, where each leg ends, the road it was
// planned on before the truck departed from it, and the stretches it drives
// empty. Everything the layer measures against comes from here.
export type RouteShape = {
  path: MapPoint[];
  cumulative: number[];
  anchors: number[];
  referencePath: MapPoint[];
  // The planned road never meets the one being driven, so it is drawn as a
  // line of its own instead of as the head of the travelled line.
  referenceApart: boolean;
  empties: { start: number; end: number }[];
};

export function routeShape(value: MapPlan): RouteShape {
  const { path, cumulative, anchors } = routeGeometry(value.route.legs);
  let referencePath: MapPoint[] = [];
  let referenceApart = false;
  if (value.fromCurrentPosition && value.referenceRoute?.legs?.length) {
    const reference = routeGeometry(value.referenceRoute.legs);
    const origin = path[0];
    const match =
      origin &&
      routePosition(
        { latitude: origin.lat, longitude: origin.lng },
        reference.path,
        reference.cumulative,
        1,
        reference.path.length,
      );
    if (match) referencePath = reference.path.slice(0, match.segment);
    else {
      referencePath = reference.path;
      referenceApart = true;
    }
  }
  const empties = emptyRouteLegs(value).flatMap((empty, index) =>
    empty
      ? [{ start: index === 0 ? 0 : anchors[index - 1], end: anchors[index] }]
      : [],
  );
  return { path, cumulative, anchors, referencePath, referenceApart, empties };
}
