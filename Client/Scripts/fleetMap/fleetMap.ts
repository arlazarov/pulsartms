import { releaseAll } from './lifecycle/release.ts';
import { createRouteLayer } from './routes/routeLayer.ts';
import { orderedStops } from './routes/pendingStops.ts';
import { loadGoogleMaps } from './provider/googleMapsLoader.ts';
import { createTruckLayer } from './trucks/truckLayer.ts';
import { createStationLayer } from './stations/stationLayer.ts';
import { fuelRecommendations } from './stations/fuelRecommendations.ts';
import { yieldToBrowser } from './lifecycle/backgroundWork.ts';
import { createNextLoadsLayer } from './routes/nextLoads.ts';
import * as payload from './geometry/encodedPath.ts';
import { createMapHost } from './provider/mapHost.ts';
import { mergeRoutePayload } from './routes/routePayload.ts';
import { createStopEtaWindow } from './routes/stopEtaWindow.ts';
import { watchInspectorScroll } from './ui/inspectorScroll.ts';
import { setStopBadges } from './routes/stopBadges.ts';
import { createCameraViewport } from './ui/cameraViewport.ts';
import { createFuelEditorFocus } from './ui/fuelEditorFocus.ts';
import { createDockedDetails } from './ui/dockedDetails.ts';
import { createRouteEditor } from './routes/routeEditor.ts';
import { distanceLabel } from './ui/distanceLabel.ts';
import type { MapPlan, RoutePayload, RouteProgress } from './contracts.d.ts';

// The load a truck could take next, and the work it is on now - which is
// what a selection on the map is reported against.
type NextLoadIdentity = {
  truckId: string;
  currentDispatchId: string;
  currentExecutionLegId: string | null;
  currentAssignmentRevision: number;
};

// Only the provider map is retained; fleet state belongs to the current mount.
const mountMap = createMapHost(
  (host, options) => new google.maps.Map(host, options),
  map => google.maps.event.clearInstanceListeners(map),
);

// Street zoom for a chosen stop; satellite imagery starts here too.
const stopZoom = 15;
// How soon a second press on the same stop counts as a double press.
const stopPressWindow = 500;

// Mainland USA and southern Canada, the fleet's working area, with room
// around it: the map now lies under the workspace's panels, so the whole
// area must still fit in the part left free (the owner, September 27).
export const fleetBounds = { north: 70, south: 14, west: -145, east: -45 };

const schemeOf = (element: HTMLElement) =>
  element.ownerDocument?.documentElement?.dataset?.theme === 'dark'
    ? 'DARK'
    : 'LIGHT';

export async function createFleetMap(
  element: HTMLElement,
  apiKey: string,
  callbacks: {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
  } | null,
) {
  let disposed = false;
  let optionsVersion = 0;
  let fuelEditing = false;
  let distanceUnit = 'both';
  const formatDistance = (miles: number) => distanceLabel(miles, distanceUnit);
  function notify(method: string, ...args: unknown[]) {
    if (disposed || !callbacks) return;
    callbacks.invokeMethodAsync(method, ...args).catch((error: unknown) => {
      if (!disposed) console.warn('[Fleet map] Blazor callback failed', error);
    });
  }
  const [, gpuModule] = await Promise.all([
    loadGoogleMaps(apiKey),
    import('./rendering/gpuScene.ts').catch(error => {
      if (
        !String(error?.message).includes(
          'Failed to fetch dynamically imported module',
        )
      )
        throw error;
      const retryUrl = `./rendering/gpuScene.js?retry=${Date.now()}`;
      return import(retryUrl);
    }),
  ]);
  await yieldToBrowser();
  if (!element?.isConnected)
    throw new Error('Map container is no longer available.');

  const mountedMap = mountMap(element, {
    center: { lat: 41.5, lng: -87.5 },
    zoom: 5,
    // The fleet works in mainland Canada and the USA: the camera's centre
    // keeps to them, and the map zooms out as far as the whole continent -
    // enough for a load from coast to coast in the part of the map the
    // panels leave free (the owner, September 27) - but not to the world.
    restriction: {
      latLngBounds: fleetBounds,
      strictBounds: false,
    },
    minZoom: 3,
    mapId: 'DEMO_MAP_ID',
    mapTypeId: 'roadmap',
    colorScheme: schemeOf(element),
    clickableIcons: false,
    draggableCursor: 'default',
    draggingCursor: 'default',
    gestureHandling: 'greedy',
    renderingType: google.maps.RenderingType.VECTOR,
    isFractionalZoomEnabled: true,
    tilt: 0,
    heading: 0,
    tiltInteractionEnabled: false,
    headingInteractionEnabled: false,
    streetViewControl: false,
    mapTypeControl: false,
    fullscreenControl: false,
    cameraControl: false,
    zoomControl: false,
    // No keyboard-shortcuts link in the corner (the owner, September 27);
    // Google's own data credit and Terms link are required and stay.
    keyboardShortcuts: false,
  });
  const map = mountedMap.map;
  const cleanup = [() => mountedMap.release()];
  // Google sets a map's colour scheme only when the map is made: a theme
  // switch asks the page to make this map again in the new scheme, in
  // place, carrying the camera and the selection over; nothing reloads.
  const scheme = schemeOf(element);
  const root = element.ownerDocument?.documentElement;
  if (root && typeof MutationObserver !== 'undefined') {
    const themeWatch = new MutationObserver(() => {
      if (!disposed && schemeOf(element) !== scheme)
        notify('OnMapSchemeChanged');
    });
    themeWatch.observe(root, {
      attributes: true,
      attributeFilter: ['data-theme'],
    });
    cleanup.push(() => themeWatch.disconnect());
  }
  function dispose() {
    if (disposed) return;
    disposed = true;
    releaseAll(cleanup.reverse());
    cleanup.length = 0;
  }
  // Everything from here on is inside the try: a mount that is not released
  // stays mounted, and every later attempt is then refused until a reload.
  try {
    const cameraViewport = createCameraViewport(element, map);
    cleanup.push(() => cameraViewport.dispose());
    cleanup.push(watchInspectorScroll(element.parentElement));
    const viewport = element.ownerDocument?.defaultView;
    let inspectionTruckId: string | null = null;
    const inspector = createDockedDetails(
      element.parentElement?.querySelector?.<HTMLElement>(
        '.fleet-map-inspector__native',
      ) ?? null,
      (kind, revision) =>
        notify('OnMapInspectorChanged', kind, inspectionTruckId, revision),
      () => (inspectionTruckId ? 'truck' : 'closed'),
      element,
      // A stop or a station the card now shows is brought out from under
      // the card, if the card opened over it.
      position => cameraViewport.reveal(position),
    );
    cleanup.push(() => inspector.dispose());
    const fuelFocus = createFuelEditorFocus(element, {
      disposed: () => disposed,
      editing: () => fuelEditing,
      plan: () => currentPlan,
      routeEditing: () => routeEditor.active,
      centerOn: position => {
        cameraViewport.refresh();
        map.panTo(cameraViewport.center(position));
      },
      fitRoute: () => route.fitRemaining(),
    });
    cleanup.push(() => fuelFocus.release());
    await yieldToBrowser();
    const gpuScene = gpuModule?.createGpuScene(map);
    cleanup.push(() => gpuScene?.dispose());
    await yieldToBrowser();
    // A P or D chosen on the map or in the chain. One press opens it and
    // keeps the zoom, only bringing a hidden stop into view; a second
    // press on the same stop soon after puts it in the middle at street
    // zoom, where the satellite policy takes over (the owner, September
    // 27). A camera move is the reader's own, so Follow ends; editors keep
    // theirs.
    let lastStopPress: { key: string; at: number } | null = null;
    // Set by a press in the trip chain: there one press shows the stop's
    // whole trip instead (the owner, September 27); a double press still
    // takes the stop to street zoom.
    let chainFit: (() => void) | null = null;
    function focusStop(position: google.maps.LatLngLiteral) {
      const fit = chainFit;
      chainFit = null;
      if (disposed || fuelEditing || routeEditor.active) return;
      const key = `${position.lat},${position.lng}`;
      const now = Date.now();
      const repeated =
        lastStopPress?.key === key && now - lastStopPress.at < stopPressWindow;
      lastStopPress = repeated ? null : { key, at: now };
      // The middle is the middle of the map left free by the panels over it.
      cameraViewport.refresh();
      if (repeated) {
        trucks.releaseCamera();
        map.moveCamera({
          center: cameraViewport.center(position, stopZoom),
          zoom: stopZoom,
        });
        return;
      }
      if (fit) {
        trucks.releaseCamera();
        fit();
        return;
      }
      // One press: a stop off the map or under a panel is brought the
      // least distance into the free part; one already there stays put.
      if (map.getBounds?.()?.contains(position) === false)
        trucks.releaseCamera();
      cameraViewport.reveal(position);
    }
    // All of a later load's drawn road, or a set of places, in the part of
    // the map the panels leave free.
    function fitPoints(points: google.maps.LatLngLiteral[] | null) {
      if (disposed || !points?.length) return;
      const bounds = new google.maps.LatLngBounds();
      for (const point of points) bounds.extend(point);
      trucks.releaseCamera();
      cameraViewport.refresh();
      map.fitBounds(bounds, cameraViewport.padding(55));
    }
    function fitLoadRoad(loadId: string, executionLegId?: string) {
      fitPoints(nextLoads.geometryOf(loadId, executionLegId));
    }
    const route = createRouteLayer(
      map,
      (truckId, miles, remaining) => {
        currentProgress = { progressMiles: miles };
        stations.setProgress(miles);
        notify('OnRouteProgress', truckId, remaining, miles);
      },
      position => {
        inspector.activate('stop');
        stations.closePopup();
        nextLoads.clearSelection();
        focusStop(position);
      },
      gpuScene?.Polyline,
      gpuScene?.StopMarker,
      inspector.popupFactory('stop'),
      () => !fuelEditing && !trucks.isFollowing(),
      base => {
        trucks.clearViewportFocus?.();
        cameraViewport.refresh();
        return cameraViewport.padding(base);
      },
      formatDistance,
    );
    cleanup.push(() => route.dispose());
    const traffic = new google.maps.TrafficLayer();
    cleanup.push(() => traffic.setMap(null));
    let nextLoadIdentity: NextLoadIdentity | null = null;
    const nextLoads = createNextLoadsLayer(
      map,
      gpuScene.Polyline,
      gpuScene.StopMarker,
      (id, stopIndex, executionLegId) => {
        if (inspector.suspended) return;
        if (id !== null) {
          inspectionTruckId = nextLoadIdentity?.truckId ?? inspectionTruckId;
          inspector.setMode('next-stop');
          route.closePopup();
          stations.closePopup();
        } else if (inspector.mode === 'next-stop')
          inspector.setMode(inspectionTruckId ? 'truck' : 'closed');
        if (nextLoadIdentity) {
          const scope =
            executionLegId ||
            nextLoadIdentity.currentExecutionLegId ||
            nextLoadIdentity.currentAssignmentRevision;
          notify(
            scope ? 'OnNextExecutionLegSelected' : 'OnNextLoadSelected',
            nextLoadIdentity.truckId,
            nextLoadIdentity.currentDispatchId,
            id,
            stopIndex,
            ...(scope
              ? [
                  executionLegId,
                  nextLoadIdentity.currentExecutionLegId ?? null,
                  nextLoadIdentity.currentAssignmentRevision ?? 0,
                ]
              : []),
          );
        }
      },
      // A load picked on the map is one the dispatcher wants to look at, and
      // its badges stand at its own pickup and delivery - which can be a day's
      // drive from the truck. The camera moves only when none of the load is
      // on the screen already, and never while the map is following or being
      // edited: otherwise picking a road under the cursor would jump away
      // from the very place that was being looked at.
      geometry => {
        if (!geometry?.length || fuelEditing || routeEditor.active) return;
        if (trucks.isFollowing()) return;
        const view = map.getBounds?.();
        if (view && geometry.some(point => view.contains(point))) return;
        const bounds = new google.maps.LatLngBounds();
        for (const point of geometry) bounds.extend(point);
        trucks.clearViewportFocus?.();
        cameraViewport.refresh();
        map.fitBounds(bounds, cameraViewport.padding(55));
      },
      position => focusStop(position),
    );
    cleanup.push(() => nextLoads.dispose());
    // Where the camera came to rest, for the page's address.
    const idle = map.addListener?.('idle', () => {
      const center = map.getCenter?.();
      const zoom = map.getZoom?.();
      if (center && typeof zoom === 'number')
        notify('OnMapViewChanged', center.lat(), center.lng(), zoom);
    });
    cleanup.push(() => idle?.remove());
    nextLoads.setVisible(false);
    traffic.setMap(map);
    const trucks = createTruckLayer(
      map,
      id => {
        inspectionTruckId = id ?? null;
        inspector.setMode(id ? 'truck' : 'closed');
        nextLoads.clearSelection();
        route.closePopup();
        stations.closePopup();
        if (id) {
          // The truck's card opens over the map; the truck comes out from
          // under it.
          const at = trucks.getPosition(id);
          if (at)
            cameraViewport.reveal({ lat: at.latitude, lng: at.longitude });
          notify('OnTruckSelected', id);
        } else cameraViewport.forget();
      },
      (id, position) => route.setRenderedPosition(id, position),
      (id, position) => route.getDisplayPosition(id, position),
      gpuScene?.createTruckMarker,
      following => notify('OnFollowChanged', following),
      (change, waitForIdle) => mountedMap.initialCamera(change, waitForIdle),
      cameraViewport,
    );
    cleanup.push(() => trucks.dispose());
    gpuScene.setClusterSelect?.(() => {
      trucks.releaseCamera();
      return cameraViewport.padding(70);
    });
    cameraViewport.onChange(() => trucks.refreshViewport?.());
    const stations = createStationLayer(
      map,
      () => {
        inspector.activate('fuel');
        route.closePopup();
        nextLoads.clearSelection();
      },
      gpuScene?.createStationPointLayer,
      inspector.popupFactory('fuel'),
      selection =>
        notify(
          'OnFuelStationEdit',
          selection.truckId,
          selection.dispatchId,
          selection.stationId,
          selection.name,
          selection.beforeStopId,
          selection.addNew,
        ),
      formatDistance,
    );
    cleanup.push(() => stations.dispose());
    const routeEditor = createRouteEditor(
      map,
      gpuScene.Polyline,
      gpuScene.StopMarker,
      google.maps.marker?.AdvancedMarkerElement,
      notify,
      base => {
        cameraViewport.refresh();
        return cameraViewport.padding(base);
      },
    );
    cleanup.push(() => routeEditor.dispose());
    const clickListener = map.addListener(
      'click',
      (event: { latLng?: unknown }) => {
        // Overlay picking can run after the provider's map listener.
        queueMicrotask(() => {
          if (disposed || gpuScene?.consumeTruckClick()) return;
          if (routeEditor.click(event)) return;
          if (stations.handleMapClick(event)) {
            route.closePopup();
            return;
          }
          if (inspector.suspended || inspector.mode === 'closed') return;
          notify(
            'OnMapBackgroundClicked',
            inspectionTruckId,
            inspector.revision,
          );
        });
      },
    );
    cleanup.push(() => clickListener.remove());
    // Satellite imagery at close zoom, the road map further out.
    const mapTypeListener = map.addListener('idle', () => {
      const mapType = (map.getZoom() ?? 0) >= 15 ? 'hybrid' : 'roadmap';
      if (map.getMapTypeId() !== mapType) map.setMapTypeId(mapType);
      // The switch is observable on the map element; the imagery keeps its
      // natural brightness in both themes (the owner, September 28).
      element.classList.toggle('is-satellite', mapType === 'hybrid');
    });
    cleanup.push(() => mapTypeListener.remove());
    // The stop whose card was open before a theme switch made this map.
    let restoringStop: string | null = null;
    function reopenRestoredStop() {
      const stopId = restoringStop;
      restoringStop = null;
      if (!stopId) return;
      inspector.activate('stop', true);
      route.restoreStop(stopId);
    }
    let routeVersion = 0;
    let fuelRecommendationKey = '';
    let currentPlan: MapPlan | null = null;
    let currentProgress: RouteProgress | null = null;
    let nextLoadsVisible = false;
    function refreshFuelRecommendations() {
      const recommendations = fuelRecommendations(
        currentPlan,
        currentProgress,
        nextLoadsVisible,
      );
      if (recommendations.key === fuelRecommendationKey) return;
      fuelRecommendationKey = recommendations.key;
      return stations.setRecommended(recommendations.stops);
    }
    const etas = createStopEtaWindow(
      (labels, refreshing) => route.setEtas(labels, refreshing),
      () => disposed,
    );
    cleanup.push(() => etas.stop());
    cleanup.push(() => {
      currentPlan = null;
      currentProgress = null;
    });

    return {
      setInspectionSuspended(value: unknown) {
        if (disposed) return;
        inspector.setSuspended(value === true);
        if (value) {
          route.closePopup();
          stations.closePopup();
          nextLoads.clearSelection();
        }
      },
      setInspectorMode(kind: string, truckId = inspectionTruckId) {
        if (disposed || !['truck', 'next-stop', 'closed'].includes(kind))
          return;
        inspectionTruckId = truckId;
        inspector.setMode(kind, true);
        route.closePopup();
        stations.closePopup();
        if (kind !== 'next-stop') nextLoads.clearSelection();
      },
      showRoute(truckId: string | null) {
        if (
          disposed ||
          inspector.suspended ||
          fuelEditing ||
          routeEditor.active ||
          !truckId ||
          truckId !== inspectionTruckId ||
          truckId !== currentPlan?.truckId ||
          currentPlan?.tracking?.allStopsPassed
        )
          return;
        fuelFocus.cancel();
        trucks.setFollow(truckId, false);
        trucks.clearViewportFocus?.();
        cameraViewport.refresh();
        route.fitRemaining();
      },
      clearMapInspection() {
        if (disposed) return;
        inspector.setMode('closed', true);
        cameraViewport.forget();
        route.closePopup();
        stations.closePopup();
        nextLoads.clearSelection();
      },
      finishInitialView() {
        if (!disposed) mountedMap.show();
      },
      setFuelEditorTruck(truckId: string | null) {
        if (
          disposed ||
          (truckId != null &&
            (truckId !== currentPlan?.truckId ||
              currentPlan?.tracking?.allStopsPassed))
        )
          return;
        if (truckId != null) fuelFocus.cancel();
        trucks.setEditingTruck(truckId);
      },
      setRouteEditor(bytes: Uint8Array | null) {
        if (disposed) return;
        const editing = payload.parseRouteEditorPayload(bytes);
        routeEditor.set(editing);
        if (routeEditor.active) fuelFocus.cancel();
        gpuScene.setRouteEditing?.(routeEditor.active);
        trucks.setEditingTruck(routeEditor.truckId);
      },
      openStation(id: string) {
        if (disposed || inspector.suspended) return false;
        return stations.openStation(id);
      },
      closeStationPopup(captureEditorFocus = false) {
        if (disposed) return;
        if (captureEditorFocus) fuelFocus.captureOpener();
        stations.closePopup();
      },
      focusFuelStation(station: any) {
        if (
          disposed ||
          !currentPlan ||
          currentPlan.tracking?.allStopsPassed ||
          station?.truckId !== currentPlan.truckId ||
          station?.dispatchId !== currentPlan.dispatchId
        )
          return false;
        const position = stations.setEditing(station);
        fuelEditing = !!position;
        if (!position) {
          fuelFocus.cancel();
          return false;
        }
        trucks.setFollow(currentPlan.truckId, false);
        route.closePopup();
        stations.closePopup();
        nextLoads.clearSelection();
        fuelFocus.focusOn(position, station);
        return true;
      },
      clearFuelStationFocus(
        restoreFocus = false,
        returnToRoute: { truckId?: string; dispatchId?: string } | null = null,
      ) {
        if (disposed) return;
        fuelEditing = false;
        fuelFocus.cancel();
        stations.setEditing(null);
        if (restoreFocus) fuelFocus.restoreFocus();
        if (returnToRoute) fuelFocus.returnToRoute(returnToRoute);
      },
      clearNextLoads() {
        if (!disposed) {
          nextLoads.clear();
          nextLoadIdentity = null;
        }
      },
      // A next load's stop named by the page's address, opened when that
      // load is drawn, exactly as if it had been picked.
      selectNextStop(
        loadId: string,
        stopIndex: number,
        executionLegId?: string | null,
        fromChain = false,
      ) {
        if (disposed) return;
        if (fromChain)
          chainFit = () => fitLoadRoad(loadId, executionLegId ?? undefined);
        nextLoads.selectStop(loadId, stopIndex, executionLegId ?? undefined);
      },
      clearNextLoadSelection() {
        if (!disposed) nextLoads.clearSelection();
      },
      focusRouteStop(stopId: string | null) {
        if (!disposed) route.focusStop(stopId ?? null);
      },
      // A stop chosen in the trip chain: its card opens as from its badge,
      // and the camera goes to it exactly as a badge press takes it.
      // A theme switch makes the map again: the stop whose card is open is
      // handed from the old map to the new one, which reopens that same
      // stop's card without moving the camera.
      selectedRouteStop() {
        return disposed ? null : route.selectedStop();
      },
      restoreRouteStop(stopId: string | null) {
        if (disposed || !stopId) return;
        // The card belongs to the plan's truck: it waits for the route.
        restoringStop = stopId;
        if (currentPlan) reopenRestoredStop();
      },
      openRouteStop(stopId: string) {
        if (disposed) return;
        chainFit = () => route.fitRemaining();
        route.openStop(stopId);
        chainFit = null;
      },
      // A later load's stop chosen in the chain while its road is not drawn:
      // one press shows all of the load's stops, a double press the stop.
      centerStop(lat: number, lng: number, stops?: number[][] | null) {
        if (!Number.isFinite(lat) || !Number.isFinite(lng)) return;
        const places = (stops ?? []).filter(
          point => Number.isFinite(point?.[0]) && Number.isFinite(point?.[1]),
        );
        if (places.length > 1)
          chainFit = () =>
            fitPoints(places.map(([pLat, pLng]) => ({ lat: pLat, lng: pLng })));
        focusStop({ lat, lng });
      },
      // A later load chosen whole in the chain: all of its road in view.
      fitNextLoad(loadId: string, executionLegId?: string | null) {
        if (disposed) return;
        nextLoads.pickLoad(loadId, executionLegId ?? undefined);
        fitLoadRoad(loadId, executionLegId ?? undefined);
      },
      setStopCompletions(list: { id: string; at: string | null }[]) {
        if (!disposed) route.setCompletions(list);
      },
      // The chain's stop badges, one label per stop id, from the page.
      setStopBadges(record: unknown) {
        if (!disposed) setStopBadges(record);
      },
      setLoadReference(payload: any) {
        if (!disposed) route.setLoadReference(payload);
      },
      setDistanceUnit(value: string) {
        if (disposed) return;
        const next =
          value === 'miles' || value === 'kilometers' ? value : 'both';
        if (next === distanceUnit) return;
        distanceUnit = next;
        route.refreshDistances();
        stations.refreshDistances();
      },
      setStopEtas(payload: any) {
        if (!disposed) etas.receive(payload, currentPlan);
      },
      setNextLoadsVisible(visible: unknown) {
        if (disposed) return;
        nextLoadsVisible = visible === true;
        nextLoads.setVisible(nextLoadsVisible);
        return refreshFuelRecommendations();
      },
      setNextLoadsBytes(bytes: Uint8Array) {
        if (!disposed) {
          const loads = payload.parseNextLoads(bytes);
          if (Array.isArray(loads)) nextLoads.set(loads);
          else {
            if (loads.truckId && loads.currentDispatchId) {
              if (
                nextLoadIdentity &&
                (loads.truckId !== nextLoadIdentity.truckId ||
                  loads.currentDispatchId !==
                    nextLoadIdentity.currentDispatchId ||
                  (loads.currentExecutionLegId ?? null) !==
                    (nextLoadIdentity.currentExecutionLegId ?? null) ||
                  (loads.currentAssignmentRevision ?? 0) !==
                    (nextLoadIdentity.currentAssignmentRevision ?? 0))
              )
                nextLoads.clear();
              nextLoadIdentity = {
                truckId: loads.truckId,
                currentDispatchId: loads.currentDispatchId,
                currentExecutionLegId: loads.currentExecutionLegId ?? null,
                currentAssignmentRevision: loads.currentAssignmentRevision ?? 0,
              };
            }
            if (loads.routes) nextLoads.set(loads.routes);
          }
        }
      },
      async setRouteBytes(
        bytes: Uint8Array,
        progress: RouteProgress | null,
        fit: boolean,
      ): Promise<boolean> {
        return this.setRoute(payload.parseRoutePayload(bytes), progress, fit);
      },
      async setRoute(
        payload: RoutePayload,
        progress: RouteProgress | null,
        fit: boolean,
      ): Promise<boolean> {
        if (disposed) return false;
        const merged = mergeRoutePayload(currentPlan, payload);
        if (!merged.accepted) return false;
        const version = ++routeVersion;
        await yieldToBrowser();
        if (version !== routeVersion || disposed) return false;
        const plan = merged.plan;
        if (!disposed) {
          etas.forgetIfPlanChanged(plan);
          if (
            plan?.truckId !== currentPlan?.truckId ||
            plan?.dispatchId !== currentPlan?.dispatchId ||
            (plan?.executionLegId ?? null) !==
              (currentPlan?.executionLegId ?? null) ||
            (plan?.assignmentRevision ?? 0) !==
              (currentPlan?.assignmentRevision ?? 0) ||
            plan?.tracking?.allStopsPassed
          ) {
            fuelEditing = false;
            trucks.setEditingTruck(routeEditor.truckId);
            fuelFocus.cancel();
          }
          currentPlan = plan;
          inspectionTruckId = plan?.truckId ?? inspectionTruckId;
          stations.setEditContext(
            plan?.truckId && plan?.dispatchId && !plan.tracking?.allStopsPassed
              ? { truckId: plan.truckId, dispatchId: plan.dispatchId }
              : null,
          );
          currentProgress = progress;
          route.setPlan(plan, fit, progress);
          if (restoringStop && plan) reopenRestoredStop();
          if (etas.held()) etas.refresh();
          nextLoads.setStopOffset(orderedStops(plan).length);
          if (plan?.truckId)
            route.setRenderedPosition(
              plan.truckId,
              trucks.getPosition(plan.truckId),
              true,
            );
          await refreshFuelRecommendations();
          if (
            !disposed &&
            version === routeVersion &&
            Number.isFinite(progress?.progressMiles)
          )
            stations.setProgress(progress!.progressMiles!);
        }
        return !disposed && version === routeVersion;
      },
      clearSelection() {
        if (disposed) return;
        routeEditor.set(null);
        gpuScene.setRouteEditing?.(false);
        inspectionTruckId = null;
        inspector.setMode('closed');
        cameraViewport.forget();
        etas.clear();
        routeVersion++;
        currentPlan = null;
        fuelEditing = false;
        trucks.setEditingTruck(null);
        fuelFocus.cancel();
        stations.setEditContext(null);
        currentProgress = null;
        nextLoads.clear();
        trucks.clearSelection();
        stations.closePopup();
        route.setPlan(null, false);
        route.setProgress(null);
        fuelRecommendationKey = '';
        return stations.setRecommended([]);
      },
      setTrucks(data: any[], points: any[]) {
        if (!disposed) trucks.setTrucks(data, points);
      },
      focusTruck(id: string, zoom?: number, preserveUserCamera?: boolean) {
        if (!disposed) cameraViewport.refresh();
        const focused =
          !disposed && trucks.focusTruck(id, zoom, preserveUserCamera);
        if (focused) {
          inspectionTruckId = id;
          inspector.setMode('truck');
          // Opened from another page - Dispatch's map link - the card is as
          // much about this truck as one picked on the map: it comes out
          // from under the card, and again when Details reshapes it.
          const at = trucks.getPosition(id);
          if (at)
            cameraViewport.reveal({ lat: at.latitude, lng: at.longitude });
          route.closePopup();
          stations.closePopup();
          nextLoads.clearSelection();
        }
        return focused;
      },
      setFollow(id: string, enabled?: boolean) {
        if (!disposed) cameraViewport.refresh();
        return !disposed && trucks.setFollow(id, enabled);
      },
      setStations(data: unknown, date: string, useIfta: unknown) {
        if (!disposed) return stations.setStations(data, date, useIfta);
      },
      setPriceOverview(data: unknown, date: string, useIfta: unknown) {
        if (!disposed) return stations.setPriceOverview(data, date, useIfta);
      },
      setStationsVisible(value: unknown) {
        if (!disposed) return stations.setVisible(value);
      },
      setIfta(value: unknown) {
        if (!disposed) return stations.setIfta(value);
      },
      setTrafficVisible(value: unknown) {
        if (!disposed) traffic.setMap(value ? map : null);
      },
      async setOptions(options: any) {
        if (disposed) return;
        const version = ++optionsVersion;
        trucks.setInitialTruck(options?.initialTruckId);
        trucks.setInitialView(options?.initialView);
        traffic.setMap(options?.trafficVisible === true ? map : null);
        await stations.setIfta(options?.useIfta === true);
        if (disposed || version !== optionsVersion) return;
        await stations.setVisible(options?.stationsVisible === true);
      },
      dispose,
    };
  } catch (error) {
    dispose();
    throw error;
  }
}
