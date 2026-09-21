import type {
  LoadReference,
  MapPlan,
  RoutePoint,
  RouteProgress,
} from '../contracts.d.ts';
import type { RouteLineFactory } from './routeRoad.ts';
import type { TruckPoint } from '../trucks/truckPoints.ts';
import type { StopEtaLabel } from './stopEtaLabels.ts';
import { releaseAll } from '../lifecycle/release.ts';
import { createProgressReports } from './progressReports.ts';
import { createRouteRoad } from './routeRoad.ts';
import { createRouteStops } from './routeStops.ts';
import { createRouteSnapper } from '../trucks/routeSnap.ts';
import { createDetailsCard } from '../ui/detailsCard.ts';
import { pendingStops } from './pendingStops.ts';
import { emptyRouteLegs } from './emptyRouteLegs.ts';
import { stopEtaIdentity } from './stopEtaIdentity.ts';

/**
 * The road a truck is driving, its stops, and what it has covered. The road
 * itself is drawn by routeRoad; what this layer decides is when the truck
 * has moved far enough for the road, the stops and the page to be told.
 *
 * @param onOpen a stop's card opened here; which stop it is stays this
 *   layer's business
 */
export function createRouteLayer(
  map: google.maps.Map,
  onProgress: (
    truckId: string,
    miles: number,
    remaining: number,
  ) => void = () => {},
  onOpen: () => void = () => {},
  Polyline?: RouteLineFactory,
  StopMarker?: any,
  popupFactory: (
    map: google.maps.Map,
    options: { onClose: () => void },
  ) => {
    show(content: Node, position?: unknown): void;
    hide(): void;
    dispose(): void;
  } = createDetailsCard,
  canFit: () => boolean = () => true,
  fitPadding: (value: number) => number | google.maps.Padding = value => value,
  formatDistance?: (miles: number) => string,
) {
  let disposed = false;
  const road = createRouteRoad(map, Polyline!);
  const idleListener = map.addListener('idle', () => {
    road.updateLineWidth();
    if (road.refreshDetail()) fitRoute();
  });
  const popup = popupFactory(map, { onClose: () => stops.close() });
  const stops = createRouteStops(
    map,
    StopMarker,
    popup,
    onOpen,
    formatDistance,
  );
  let plan: MapPlan | null = null;
  let identity = '';
  let stopIdentity = '';
  let etaIdentity = '';
  let serverProgress: RouteProgress | null = null;
  let lastPositionUpdate = 0;
  let lastMatchedPositionAt: number | null = null;
  let matchedSegment: number | null = null,
    lastFullMatchAt = -Infinity;
  let pendingFit = false;
  // The road follows the truck every frame; the mileage beside it steps.
  const reports = createProgressReports(onProgress, miles =>
    stops.setProgress(miles),
  );
  const dragListener = map.addListener('dragstart', () => {
    pendingFit = false;
  });
  const snapPosition = createRouteSnapper();

  function getDisplayPosition(truckId: string, position: TruckPoint) {
    if (disposed) return position;
    if (!plan || truckId !== plan.truckId) return position;
    return snapPosition(
      position,
      `${truckId}:${plan.id}:${plan.version}:` +
        `${plan.executionLegId ?? ''}:${plan.assignmentRevision ?? 0}`,
      () => road.match(position, serverProgress?.progressMiles),
      performance.now(),
    );
  }

  function setPlan(
    value: MapPlan | null,
    fit?: boolean,
    progress?: RouteProgress | null,
  ) {
    if (disposed) return;
    const key = value
      ? `${value.truckId}:${value.dispatchId}:${value.id}:${value.version}:` +
        `${value.executionLegId ?? ''}:${value.assignmentRevision ?? 0}:` +
        emptyRouteLegs(value).join(',')
      : '';
    const nextEtaIdentity = stopEtaIdentity(value);
    const preserveStops = value !== null && nextEtaIdentity === etaIdentity;
    etaIdentity = nextEtaIdentity;
    const nextStops = `${value?.tracking?.nextStopId ?? ''}:${pendingStops(
      value,
    )
      .map(stop => stop.id)
      .join(':')}`;
    if (key !== identity || nextStops !== stopIdentity) reports.sayNext();
    stopIdentity = nextStops;
    if (key === identity) {
      if (!preserveStops) {
        stops.setEtas(new Map());
        reports.setWaiting(false);
      }
      plan = value;
      stops.setPlan(value);
      if (fit) pendingFit = canFit();
      if (progress !== undefined) setProgress(progress);
      if (fit) fitRoute();
      return;
    }
    identity = key;
    if (!preserveStops) {
      stops.clear();
      reports.forget();
    }
    reports.setTotal(
      value?.route.legs.reduce((sum, leg) => sum + leg.miles, 0) ?? 0,
    );
    serverProgress = null;
    pendingFit = Boolean(value && fit && canFit());
    lastMatchedPositionAt = null;
    matchedSegment = null;
    lastFullMatchAt = -Infinity;
    plan = value;
    road.setRoute(value);
    if (!value) return;
    stops.setPlan(value);
    setProgress(progress ?? null);
  }

  function fitRoute() {
    if (!pendingFit || !road.isDrawn() || !Number.isFinite(road.drawnMiles()))
      return;
    pendingFit = false;
    if (!canFit()) return;
    map.fitBounds(road.bounds(), fitPadding(55));
  }

  function setProgress(
    progress: RouteProgress | null | undefined,
    fromPlayback = false,
    forceDraw = false,
  ) {
    if (disposed) return;
    if (!fromPlayback) serverProgress = progress ?? null;
    if (!plan || !road.hasPath()) return;
    // Keep the line on the playback timeline while fresh GPS progress is stored
    // for matching. Otherwise every poll briefly jumps ahead of the truck.
    if (
      !fromPlayback &&
      !forceDraw &&
      Number.isFinite(progress?.progressMiles) &&
      lastMatchedPositionAt !== null &&
      performance.now() - lastMatchedPositionAt < 1500
    )
      return;
    const nextProgress = progress?.progressMiles ?? null;
    reports.publish(plan.truckId, nextProgress, performance.now());
    // Show a cold route without GPS; retain its trimmed road on later gaps.
    // This display fallback must not report zero as measured truck progress.
    if (!Number.isFinite(nextProgress)) {
      if (!Number.isFinite(road.drawnMiles())) road.drawAt(0);
      fitRoute();
      return;
    }
    road.drawAt(nextProgress!, forceDraw);
    fitRoute();
  }

  // Where the truck is drawn now, matched onto the road so the line ends
  // under it rather than at the last thing the server said.
  function setRenderedPosition(
    truckId: string,
    position: RoutePoint | null,
    force = false,
  ) {
    if (disposed) return;
    if (
      !plan ||
      truckId !== plan.truckId ||
      !position ||
      !Number.isFinite(serverProgress?.progressMiles)
    )
      return;
    const now = performance.now();
    const interval = (map.getZoom() ?? 5) >= 13 ? 0 : 100;
    if (!force && now - lastPositionUpdate < interval) return;
    lastPositionUpdate = now;
    const miles = serverProgress!.progressMiles!;
    const fullMatch = matchedSegment === null || now - lastFullMatchAt >= 100;
    const [start, end] = fullMatch
      ? road.milesRange(miles - 5, miles + 5)
      : [
          Math.max(1, matchedSegment! - 2),
          Math.min(road.pathLength(), matchedSegment! + 3),
        ];
    if (fullMatch) lastFullMatchAt = now;
    const match = road.locate(position, start, end);
    if (match) {
      lastMatchedPositionAt = now;
      matchedSegment = match.segment;
      setProgress(
        { ...serverProgress, progressMiles: match.miles },
        true,
        force,
      );
    } else matchedSegment = null;
  }

  function dispose() {
    if (disposed) return;
    // The plan has to be cleared while this is still live - setPlan does
    // nothing once disposed, and the stop markers would stay on the map. The
    // flag is its own step so that a throw inside setPlan cannot skip it.
    releaseAll([
      () => idleListener.remove(),
      () => dragListener.remove(),
      () => setPlan(null, false),
      () => (disposed = true),
      () => (serverProgress = null),
      () => popup.dispose(),
      () => road.dispose(),
    ]);
  }

  return {
    setPlan,
    setProgress,
    setRenderedPosition,
    getDisplayPosition,
    fitRemaining() {
      if (
        disposed ||
        !plan ||
        !road.isDrawn() ||
        !Number.isFinite(road.drawnMiles()) ||
        !canFit()
      )
        return;
      pendingFit = true;
      fitRoute();
    },
    refreshDistances() {
      if (!disposed) stops.refreshDistances();
    },
    setLoadReference(value: (LoadReference & { dispatchId?: string }) | null) {
      if (!disposed) stops.setLoadReference(value);
    },
    setEtas(labels: Map<string, StopEtaLabel>, pending = false) {
      if (disposed) return;
      const resumed = reports.setWaiting(pending);
      stops.setEtas(labels);
      if (resumed) {
        reports.sayNext();
        setProgress(
          Number.isFinite(serverProgress?.progressMiles) &&
            Number.isFinite(road.drawnMiles())
            ? { ...serverProgress, progressMiles: road.drawnMiles() }
            : serverProgress,
          true,
        );
      }
    },
    closePopup() {
      if (!disposed) stops.close();
    },
    dispose,
  };
}
