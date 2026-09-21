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
    mapId: 'DEMO_MAP_ID',
    mapTypeId: 'roadmap',
    colorScheme:
      element.ownerDocument?.documentElement?.dataset?.theme === 'dark'
        ? 'DARK'
        : 'LIGHT',
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
  });
  const map = mountedMap.map;
  const cleanup = [() => mountedMap.release()];
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
    const route = createRouteLayer(
      map,
      (truckId, miles, remaining) => {
        currentProgress = { progressMiles: miles };
        stations.setProgress(miles);
        notify('OnRouteProgress', truckId, remaining, miles);
      },
      () => {
        inspector.activate('stop');
        stations.closePopup();
        nextLoads.clearSelection();
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
    );
    cleanup.push(() => nextLoads.dispose());
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
        if (id) notify('OnTruckSelected', id);
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
    const mapTypeListener = map.addListener('idle', () => {
      const mapType = (map.getZoom() ?? 0) >= 15 ? 'hybrid' : 'roadmap';
      if (map.getMapTypeId() !== mapType) map.setMapTypeId(mapType);
    });
    cleanup.push(() => mapTypeListener.remove());
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
      clearNextLoadSelection() {
        if (!disposed) nextLoads.clearSelection();
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
