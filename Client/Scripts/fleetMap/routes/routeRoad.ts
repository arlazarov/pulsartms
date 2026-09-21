import type { MapPlan, MapPoint, RoutePoint } from '../contracts.d.ts';
import type { TruckPoint } from '../trucks/truckPoints.ts';
import {
  fullRouteDetailZoom,
  routeDetailIndices,
} from '../geometry/routeDetail.ts';
import { routePosition } from '../geometry/routePosition.ts';
import { lowerBound, segmentRange } from '../geometry/routeSearch.ts';
import { matchRoute } from '../trucks/routeSnap.ts';
import { routeShape } from './routeShape.ts';

// One line of the road. Whoever draws it decides what a role looks like.
export type RouteLine = {
  setOptions(options: { strokeWeight: number }): void;
  setPath(path: MapPoint[]): void;
  getPath(): {
    removeAt(index: number): void;
    setAt(index: number, value: google.maps.LatLng): void;
  };
  setMap(map: google.maps.Map | null): void;
};

export type RouteLineFactory = new (options: {
  map?: google.maps.Map;
  routeRole: string;
  strokeWeight: number | null;
  clickable: boolean;
  zIndex: number;
}) => RouteLine;

// A stretch the truck drives empty, drawn apart from the loaded road.
type EmptySegment = {
  start: number;
  end: number;
  line: RouteLine;
  history: RouteLine;
  detailStart: number | null;
};

/**
 * The road on the map: the line still to drive, the line already driven,
 * the empty stretches, and the route the plan departed from. It holds the
 * geometry the rest of the layer measures against, and knows how far along
 * it is currently drawn - but never moves the camera and never decides when
 * the truck has moved.
 */
export function createRouteRoad(
  map: google.maps.Map,
  Polyline: RouteLineFactory,
) {
  const remaining = new Polyline({
    map,
    routeRole: 'current',
    strokeWeight: 4,
    clickable: false,
    zIndex: 2,
  });
  const future = new Polyline({
    map,
    routeRole: 'traveled',
    strokeWeight: 4,
    clickable: false,
    zIndex: 1,
  });
  const tail = new Polyline({
    map,
    routeRole: 'current',
    strokeWeight: 4,
    clickable: false,
    zIndex: 2,
  });
  const emptySegments: EmptySegment[] = [];
  let separateReference: RouteLine | null = null;
  let appliedLineWidth: number | null = null;
  let path: MapPoint[] = [];
  let referencePath: MapPoint[] = [];
  let cumulative: number[] = [];
  let anchors: number[] = [];
  let detailIndices: number[] = [];
  let detailZoom: number | null = null;
  let renderedDetailStart: number | null = null;
  let renderedStart: MapPoint | null = null;
  let drawnMiles: number | undefined = undefined;
  let hasRoute = false;

  const detailLevel = () =>
    Math.min(fullRouteDetailZoom, Math.floor(map.getZoom() ?? 5));

  function updateLineWidth() {
    const zoom = map.getZoom() ?? 5;
    const width = zoom < 7 ? 2 : zoom < 10 ? 3 : zoom < 14 ? 4 : 5;
    if (width === appliedLineWidth) return;
    appliedLineWidth = width;
    remaining.setOptions({ strokeWeight: width });
    future.setOptions({ strokeWeight: width });
    tail.setOptions({ strokeWeight: width });
    separateReference?.setOptions({ strokeWeight: width });
    for (const entry of emptySegments) {
      entry.line.setOptions({ strokeWeight: width });
      entry.history.setOptions({ strokeWeight: width });
    }
  }

  function drawHistory() {
    for (const entry of emptySegments)
      entry.history.setPath(
        detailIndices
          .filter(index => index >= entry.start && index <= entry.end)
          .map(index => path[index]),
      );
    const referenceIndices = routeDetailIndices(referencePath, detailZoom!, []);
    const referencePoints = referenceIndices.map(index => referencePath[index]);
    separateReference?.setPath(referencePoints);
    future.setPath([
      ...(separateReference ? [] : referencePoints),
      ...detailIndices.map(index => path[index]),
    ]);
  }

  // The width every line is built with is the one the current zoom asks
  // for, so a line added later matches the ones already drawn.
  updateLineWidth();

  return {
    updateLineWidth,
    hasPath: () => path.length > 0,
    pathLength: () => path.length,
    // Where the drawn road starts now, and the mileage it was drawn at.
    isDrawn: () => renderedStart !== null,
    drawnMiles: () => drawnMiles,
    bounds() {
      const bounds = new google.maps.LatLngBounds();
      for (const point of referencePath) bounds.extend(point);
      for (const point of path) bounds.extend(point);
      return bounds;
    },
    // Where a position sits on this road.
    match(position: TruckPoint, miles: number | null | undefined) {
      return matchRoute(position, path, cumulative, miles);
    },
    locate(position: RoutePoint, start: number, end: number) {
      return routePosition(position, path, cumulative, start, end);
    },
    milesRange(from: number, to: number) {
      return segmentRange(cumulative, from, to);
    },
    // A new route, or none. Everything drawn for the last one goes.
    setRoute(value: MapPlan | null) {
      for (const entry of emptySegments) {
        entry.line.setMap(null);
        entry.history.setMap(null);
      }
      emptySegments.length = 0;
      separateReference?.setMap(null);
      separateReference = null;
      renderedStart = null;
      renderedDetailStart = null;
      drawnMiles = undefined;
      hasRoute = Boolean(value);
      path = [];
      referencePath = [];
      cumulative = [];
      anchors = [];
      detailIndices = [];
      detailZoom = null;
      future.setPath([]);
      tail.setPath([]);
      if (!value) {
        remaining.setPath([]);
        return;
      }
      const shape = routeShape(value);
      ({ path, cumulative, anchors, referencePath } = shape);
      // The road the plan departed from is drawn on its own line only when
      // it never meets the one being driven.
      if (shape.referenceApart)
        separateReference = new Polyline({
          map,
          routeRole: 'traveled',
          strokeWeight: appliedLineWidth,
          clickable: false,
          zIndex: 1,
        });
      const options = { map, strokeWeight: appliedLineWidth, clickable: false };
      for (const empty of shape.empties)
        emptySegments.push({
          ...empty,
          line: new Polyline({
            ...options,
            routeRole: 'current-empty',
            zIndex: 3,
          }),
          history: new Polyline({
            ...options,
            routeRole: 'traveled-empty',
            zIndex: 1.5,
          }),
          detailStart: null,
        });
      detailZoom = detailLevel();
      detailIndices = routeDetailIndices(path, detailZoom, anchors);
      remaining.setPath([]);
      drawHistory();
    },
    // The truck has covered this many miles: the road is split there.
    drawAt(miles: number, forceDraw = false) {
      if (drawnMiles === miles && !forceDraw) return;
      drawnMiles = miles;
      const i = Math.min(lowerBound(cumulative, miles), path.length - 1);
      const prev = Math.max(0, i - 1);
      const delta = cumulative[i] - cumulative[prev];
      const t =
        delta > 0
          ? Math.max(0, Math.min(1, (miles - cumulative[prev]) / delta))
          : 0;
      const split = {
        lat: path[prev].lat + (path[i].lat - path[prev].lat) * t,
        lng: path[prev].lng + (path[i].lng - path[prev].lng) * t,
      };
      renderedStart = split;
      const detailStart = lowerBound(detailIndices, i);
      if (renderedDetailStart !== null && detailStart >= renderedDetailStart) {
        // Only the two-point leading edge changes every frame. Keep the long
        // rasterized route untouched until an actual route vertex is passed.
        if (detailStart !== renderedDetailStart) {
          if (detailStart === renderedDetailStart + 1)
            tail.getPath().removeAt(0);
          else
            tail.setPath(
              detailIndices.slice(detailStart).map(index => path[index]),
            );
          remaining
            .getPath()
            .setAt(1, new google.maps.LatLng(path[detailIndices[detailStart]]));
        }
        remaining.getPath().setAt(0, new google.maps.LatLng(split));
        renderedDetailStart = detailStart;
      } else {
        tail.setPath(
          detailIndices.slice(detailStart).map(index => path[index]),
        );
        remaining.setPath([split, path[detailIndices[detailStart]]]);
        renderedDetailStart = detailStart;
      }
      for (const entry of emptySegments) {
        const start = Math.max(entry.start, i);
        const key = lowerBound(detailIndices, start);
        if (forceDraw || entry.detailStart !== key) {
          const points = detailIndices
            .filter(index => index >= start && index <= entry.end)
            .map(index => path[index]);
          entry.line.setPath(
            start > entry.end
              ? []
              : [
                  miles > cumulative[entry.start] ? split : path[entry.start],
                  ...points,
                ],
          );
          entry.detailStart = key;
        } else if (start <= entry.end && miles > cumulative[entry.start])
          entry.line.getPath().setAt(0, new google.maps.LatLng(split));
      }
    },
    // The map has been zoomed: a coarser or finer road may be needed. Says
    // whether it redrew, because the camera is only refitted when it did.
    refreshDetail() {
      const zoom = detailLevel();
      if (!hasRoute || detailZoom === zoom) return false;
      detailZoom = zoom;
      const nextIndices = routeDetailIndices(path, zoom, anchors);
      if (
        nextIndices.length === detailIndices.length &&
        nextIndices.every((index, i) => index === detailIndices[i])
      )
        return false;
      detailIndices = nextIndices;
      drawHistory();
      renderedDetailStart = null;
      if (!Number.isFinite(drawnMiles)) return false;
      this.drawAt(drawnMiles!, true);
      return true;
    },
    dispose() {
      remaining.setMap(null);
      future.setMap(null);
      tail.setMap(null);
    },
  };
}
