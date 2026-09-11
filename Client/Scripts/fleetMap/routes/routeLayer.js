import { fullRouteDetailZoom, routeDetailIndices } from '../geometry/routeDetail.js';
import { routeGeometry } from '../geometry/routeGeometry.js';
import { createRouteStops } from './routeStops.js';
import { routePosition } from '../geometry/routePosition.js';
import { lowerBound, segmentRange } from '../geometry/routeSearch.js';
import { createRouteSnapper, matchRoute } from '../trucks/routeSnap.js';
import { createDetailsCard } from '../ui/detailsCard.js';
import { pendingStops } from './pendingStops.js';
import { stopEtaIdentity } from './stopEtaIdentity.js';

const PROGRESS_DISPLAY_INTERVAL_MS = 60_000;

export function createRouteLayer(map, onProgress = () => {}, onOpen = () => {}, Polyline, StopMarker, popupFactory = createDetailsCard, canFit = () => true, fitPadding = value => value) {
  const remaining = new Polyline({ map, routeRole: 'current', strokeWeight: 4, clickable: false, zIndex: 2 });
  const future = new Polyline({ map, routeRole: 'future', strokeWeight: 4, clickable: false, zIndex: 1 });
  const tail = new Polyline({ map, routeRole: 'current', strokeWeight: 4, clickable: false, zIndex: 2 });
  let appliedLineWidth = null;
  let disposed = false;
  const detailLevel = () => Math.min(fullRouteDetailZoom, Math.floor(map.getZoom() ?? 5));
  function updateLineWidth() {
    const zoom = map.getZoom() ?? 5;
    const width = zoom < 7 ? 2 : zoom < 10 ? 3 : zoom < 14 ? 4 : 5;
    if (width === appliedLineWidth) return;
    appliedLineWidth = width;
    remaining.setOptions({ strokeWeight: width });
    future.setOptions({ strokeWeight: width });
    tail.setOptions({ strokeWeight: width });
  }
  const idleListener = map.addListener('idle', () => { updateLineWidth(); refreshDetail(); });
  updateLineWidth();
  const popup = popupFactory(map, { onClose: () => stops.close() });
  const stops = createRouteStops(map, StopMarker, popup, onOpen);
  let plan = null;
  let path = [];
  let cumulative = [];
  let detailIndices = [], anchors = [], detailZoom = null;
  let identity = '';
  let lastProgress = undefined;
  let serverProgress = null;
  let lastPositionUpdate = 0;
  let lastMatchedPositionAt = null;
  let matchedSegment = null, lastFullMatchAt = -Infinity;
  let lastProgressNotification = -Infinity;
  let displayedProgress = null;
  let stopIdentity = '';
  let etaIdentity = '';
  let totalMiles = 0;
  let displayPending = false;
  let renderedDetailStart = null;
  let renderedStart = null, pendingFit = false;
  const dragListener = map.addListener('dragstart', () => { pendingFit = false; });
  const snapPosition = createRouteSnapper();

  function getDisplayPosition(truckId, position) {
    if (disposed) return position;
    if (!plan || truckId !== plan.truckId) return position;
    return snapPosition(position, `${truckId}:${plan.id}:${plan.version}`, () =>
      matchRoute(position, path, cumulative, serverProgress?.progressMiles), performance.now());
  }

  function setPlan(value, fit, progress) {
    if (disposed) return;
    const key = value ? `${value.truckId}:${value.dispatchId}:${value.id}:${value.version}` : '';
    const nextEtaIdentity = stopEtaIdentity(value);
    const preserveStops = value !== null && nextEtaIdentity === etaIdentity;
    etaIdentity = nextEtaIdentity;
    const nextStops = `${value?.tracking?.nextStopId ?? ''}:${pendingStops(value).map(stop => stop.id).join(':')}`;
    if (key !== identity || nextStops !== stopIdentity) lastProgressNotification = -Infinity;
    stopIdentity = nextStops;
    if (key === identity) {
      if (!preserveStops) { stops.setEtas(new Map()); displayPending = false; }
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
      displayPending = false;
      displayedProgress = null;
    }
    totalMiles = value?.route.legs.reduce((sum, leg) => sum + leg.miles, 0) ?? 0;
    lastProgress = undefined;
    serverProgress = null;
    renderedStart = null;
    pendingFit = Boolean(value && fit && canFit());
    lastMatchedPositionAt = null;
    matchedSegment = null; lastFullMatchAt = -Infinity;
    renderedDetailStart = null;
    plan = value;
    path = []; cumulative = []; anchors = []; detailIndices = []; detailZoom = null;
    future.setPath([]);
    tail.setPath([]);
    if (!value) { remaining.setPath([]); return; }
    ({ path, cumulative, anchors } = routeGeometry(value.route.legs));
    detailZoom = detailLevel();
    detailIndices = routeDetailIndices(path, detailZoom, anchors);
    remaining.setPath([]);
    stops.setPlan(value);
    if (progress !== undefined) setProgress(progress);
  }

  function fitRoute() {
    if (!pendingFit || !renderedStart || !Number.isFinite(lastProgress)) return;
    pendingFit = false;
    if (!canFit()) return;
    const bounds = new google.maps.LatLngBounds();
    bounds.extend(renderedStart);
    for (let i = lowerBound(cumulative, lastProgress); i < path.length; i++) bounds.extend(path[i]);
    map.fitBounds(bounds, fitPadding(55));
  }

  function setProgress(progress, fromPlayback = false, forceDraw = false) {
    if (disposed) return;
    if (!fromPlayback) serverProgress = progress;
    if (!plan || !path.length) return;
    // Keep the line on the playback timeline while fresh GPS progress is stored
    // for matching. Otherwise every poll briefly jumps ahead of the truck.
    if (!fromPlayback && !forceDraw && Number.isFinite(progress?.progressMiles)
      && lastMatchedPositionAt !== null && performance.now() - lastMatchedPositionAt < 1500) return;
    const nextProgress = progress?.progressMiles ?? null;
    // Display mileage is sampled separately from the continuously animated road.
    const now = performance.now();
    if (!displayPending && !Number.isFinite(nextProgress)) {
      if (displayedProgress !== null) stops.setProgress(null);
      displayedProgress = null;
      lastProgressNotification = -Infinity;
    }
    else if (!displayPending && now - lastProgressNotification >= PROGRESS_DISPLAY_INTERVAL_MS) {
      lastProgressNotification = now;
      displayedProgress = nextProgress;
      stops.setProgress(nextProgress);
      onProgress(plan.truckId, nextProgress, Math.max(0, totalMiles - nextProgress));
    }
    // Unknown progress cannot reveal a driven prefix or expand a retained warm route.
    if (!Number.isFinite(nextProgress)) { fitRoute(); return; }
    drawProgress(nextProgress, forceDraw);
  }

  function drawProgress(miles, forceDraw = false) {
    if (lastProgress === miles && !forceDraw) { fitRoute(); return; }
    lastProgress = miles;
    const i = Math.min(lowerBound(cumulative, miles), path.length - 1);
    const prev = Math.max(0, i - 1);
    const delta = cumulative[i] - cumulative[prev];
    const t = delta > 0 ? Math.max(0, Math.min(1, (miles - cumulative[prev]) / delta)) : 0;
    const split = { lat: path[prev].lat + (path[i].lat - path[prev].lat) * t, lng: path[prev].lng + (path[i].lng - path[prev].lng) * t };
    renderedStart = split;
    const detailStart = lowerBound(detailIndices, i);
    if (renderedDetailStart !== null && detailStart >= renderedDetailStart) {
      // Only the two-point leading edge changes every frame. Keep the long
      // rasterized route untouched until an actual route vertex is passed.
      if (detailStart !== renderedDetailStart) {
        const line = tail.getPath();
        for (let index = renderedDetailStart; index < detailStart; index++) line.removeAt(0);
        remaining.getPath().setAt(1, new google.maps.LatLng(path[detailIndices[detailStart]]));
      }
      remaining.getPath().setAt(0, new google.maps.LatLng(split));
      renderedDetailStart = detailStart;
    }
    else {
      tail.setPath(detailIndices.slice(detailStart).map(index => path[index]));
      remaining.setPath([split, path[detailIndices[detailStart]]]);
      renderedDetailStart = detailStart;
    }
    fitRoute();
  }

  function refreshDetail() {
    const zoom = detailLevel();
    if (!plan || detailZoom === zoom) return;
    detailZoom = zoom;
    const nextIndices = routeDetailIndices(path, zoom, anchors);
    if (nextIndices.length === detailIndices.length && nextIndices.every((index, i) => index === detailIndices[i])) return;
    detailIndices = nextIndices;
    renderedDetailStart = null;
    if (Number.isFinite(lastProgress)) drawProgress(lastProgress, true);
  }

  function setRenderedPosition(truckId, position, force = false) {
    if (disposed) return;
    if (!plan || truckId !== plan.truckId || !position || !Number.isFinite(serverProgress?.progressMiles)) return;
    const now = performance.now();
    const interval = (map.getZoom() ?? 5) >= 13 ? 0 : 100;
    if (!force && now - lastPositionUpdate < interval) return;
    lastPositionUpdate = now;
    const fullMatch = matchedSegment === null || now - lastFullMatchAt >= 100;
    const [start, end] = fullMatch
      ? segmentRange(cumulative, serverProgress.progressMiles - 5, serverProgress.progressMiles + 5)
      : [Math.max(1, matchedSegment - 2), Math.min(path.length, matchedSegment + 3)];
    if (fullMatch) lastFullMatchAt = now;
    const match = routePosition(position, path, cumulative, start, end);
    if (match) {
      lastMatchedPositionAt = now;
      matchedSegment = match.segment;
      setProgress({ ...serverProgress, progressMiles: match.miles }, true, force);
    }
    else matchedSegment = null;
  }

  function dispose() {
    if (disposed) return;
    idleListener.remove();
    dragListener.remove();
    setPlan(null, false);
    disposed = true;
    serverProgress = null;
    popup.dispose();
    remaining.setMap(null);
    future.setMap(null);
    tail.setMap(null);
  }

  return { setPlan, setProgress, setRenderedPosition, getDisplayPosition,
    setLoadReference(value) { if (!disposed) stops.setLoadReference(value); },
    setEtas(labels, pending = false) {
      if (disposed) return;
      const resumed = displayPending && !pending;
      displayPending = pending;
      stops.setEtas(labels);
      if (resumed) {
        lastProgressNotification = -Infinity;
        setProgress(Number.isFinite(serverProgress?.progressMiles) && Number.isFinite(lastProgress)
          ? { ...serverProgress, progressMiles: lastProgress } : serverProgress, true);
      }
    }, closePopup() { if (!disposed) stops.close(); }, dispose };
}
