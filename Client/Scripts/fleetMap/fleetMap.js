import { createRouteLayer } from './routes/routeLayer.js';
import { orderedStops } from './routes/pendingStops.js';
import { loadGoogleMaps } from './provider/googleMapsLoader.js';
import { createTruckLayer } from './trucks/truckLayer.js';
import { createStationLayer } from './stations/stationLayer.js';
import { fuelRecommendations } from './stations/fuelRecommendations.js';
import { yieldToBrowser } from './lifecycle/backgroundWork.js';
import { createNextLoadsLayer } from './routes/nextLoads.js';
import { createMapHost } from './provider/mapHost.js';
import { mergeRoutePayload } from './routes/routePayload.js';
import { stopEtaDeadline, stopEtaLabels } from './routes/stopEtaLabels.js';
import { etaStops, stopEtaIdentity } from './routes/stopEtaIdentity.js';
import { createCameraViewport } from './ui/cameraViewport.js';
import { createDockedDetails } from './ui/dockedDetails.js';
import { createRouteEditor } from './routes/routeEditor.js';
import { distanceLabel } from './ui/distanceLabel.js';

// Only the provider map is retained; fleet state belongs to the current mount.
const mountMap = createMapHost(
  (host, options) => new google.maps.Map(host, options),
);

export async function createFleetMap(element, apiKey, callbacks) {
  let disposed = false;
  let optionsVersion = 0;
  let fuelEditing = false;
  let distanceUnit = 'both';
  const formatDistance = miles => distanceLabel(miles, distanceUnit);
  function notify(method, ...args) {
    if (disposed || !callbacks) return;
    callbacks.invokeMethodAsync(method, ...args).catch(error => {
      if (!disposed) console.warn('[Fleet map] Blazor callback failed', error);
    });
  }
  const [, gpuModule] = await Promise.all([
    loadGoogleMaps(apiKey),
    import('./rendering/gpuScene.js').catch(error => {
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
  const cameraViewport = createCameraViewport(element, map);
  cleanup.push(() => cameraViewport.dispose());
  const viewport = element.ownerDocument?.defaultView;
  let inspectionTruckId = null;
  const inspector = createDockedDetails(
    element.parentElement?.querySelector?.('.fleet-map-inspector__native'),
    (kind, revision) =>
      notify('OnMapInspectorChanged', kind, inspectionTruckId, revision),
    () => (inspectionTruckId ? 'truck' : 'closed'),
    element,
  );
  cleanup.push(() => inspector.dispose());
  let fuelFocusFrame = null;
  let fuelFocusVersion = 0;
  let fuelEditorOpener = null;
  let fuelReturnFrame = null;
  function cancelFuelReturn() {
    if (fuelReturnFrame !== null)
      viewport?.cancelAnimationFrame(fuelReturnFrame);
    fuelReturnFrame = null;
  }
  function restoreFuelEditorFocus() {
    const document = element.ownerDocument;
    const target = fuelEditorOpener;
    fuelEditorOpener = null;
    const closingFocus = document?.activeElement;
    if (!target || !closingFocus?.closest?.('.fuel-plan-editor')) return;
    cancelFuelReturn();
    const restore = () => {
      fuelReturnFrame = null;
      if (
        disposed ||
        document.querySelector('.fuel-plan-editor') ||
        (document.activeElement !== document.body &&
          document.activeElement !== closingFocus)
      )
        return;
      const connected =
        target.isConnected && !target.closest?.('[inert]') && !target.disabled;
      (connected ? target : element).focus?.({ preventScroll: true });
    };
    if (viewport?.requestAnimationFrame)
      fuelReturnFrame = viewport.requestAnimationFrame(restore);
    else restore();
  }
  cleanup.push(() => {
    cancelFuelReturn();
    fuelEditorOpener = null;
  });
  function cancelFuelFocus() {
    fuelFocusVersion++;
    if (fuelFocusFrame !== null) viewport?.cancelAnimationFrame(fuelFocusFrame);
    fuelFocusFrame = null;
  }
  cleanup.push(cancelFuelFocus);
  function dispose() {
    if (disposed) return;
    disposed = true;
    for (const release of cleanup.reverse()) release();
    cleanup.length = 0;
  }
  try {
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
    let nextLoadIdentity = null;
    const nextLoads = createNextLoadsLayer(
      map,
      gpuScene.Polyline,
      gpuScene.StopMarker,
      (id, stopIndex, executionLegId = null) => {
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
    const clickListener = map.addListener('click', event => {
      // Overlay picking can run after the provider's map listener.
      queueMicrotask(() => {
        if (disposed || gpuScene?.consumeTruckClick()) return;
        if (routeEditor.click(event)) return;
        if (stations.handleMapClick(event)) {
          route.closePopup();
          return;
        }
        if (inspector.suspended || inspector.mode === 'closed') return;
        notify('OnMapBackgroundClicked', inspectionTruckId, inspector.revision);
      });
    });
    cleanup.push(() => clickListener.remove());
    const mapTypeListener = map.addListener('idle', () => {
      const mapType = map.getZoom() >= 15 ? 'hybrid' : 'roadmap';
      if (map.getMapTypeId() !== mapType) map.setMapTypeId(mapType);
    });
    cleanup.push(() => mapTypeListener.remove());
    let routeVersion = 0;
    let fuelRecommendationKey = '';
    /** @type {import('./contracts.d.ts').MapPlan | null} */
    let currentPlan = null;
    /** @type {import('./contracts.d.ts').RouteProgress | null} */
    let currentProgress = null;
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
    function focusFuelPosition(position, station) {
      cancelFuelFocus();
      const version = fuelFocusVersion;
      const pan = () => {
        if (
          disposed ||
          version !== fuelFocusVersion ||
          !fuelEditing ||
          station.truckId !== currentPlan?.truckId ||
          station.dispatchId !== currentPlan?.dispatchId
        )
          return;
        fuelFocusFrame = null;
        cameraViewport.refresh();
        map.panTo(cameraViewport.center(position));
      };
      // Blazor selects before committing the expanded editor layout.
      if (viewport?.requestAnimationFrame)
        fuelFocusFrame = viewport.requestAnimationFrame(pan);
      else pan();
    }
    function returnToFuelRoute(identity) {
      const version = fuelFocusVersion;
      const fit = () => {
        if (
          disposed ||
          version !== fuelFocusVersion ||
          fuelEditing ||
          routeEditor.active ||
          identity.truckId !== currentPlan?.truckId ||
          identity.dispatchId !== currentPlan?.dispatchId ||
          currentPlan?.tracking?.allStopsPassed
        )
          return;
        fuelFocusFrame = null;
        route.fitRemaining();
      };
      if (viewport?.requestAnimationFrame)
        fuelFocusFrame = viewport.requestAnimationFrame(fit);
      else fit();
    }
    let etaExpiry = null;
    let etaSnapshot = null;
    let etaRefreshing = false;
    let etaTimerVersion = 0;
    function clearStopEtas() {
      clearTimeout(etaExpiry);
      etaExpiry = null;
      etaSnapshot = null;
      etaRefreshing = false;
      etaTimerVersion++;
      route.setEtas(new Map());
    }
    function showStopEtas() {
      clearTimeout(etaExpiry);
      const version = ++etaTimerVersion;
      const remaining =
        (etaRefreshing ? etaSnapshot?.graceUntil : etaSnapshot?.validUntil) -
        Date.now();
      if (!(remaining > 0)) {
        clearStopEtas();
        return;
      }
      route.setEtas(etaSnapshot.labels, etaRefreshing);
      etaExpiry = setTimeout(
        () => {
          if (!disposed && version === etaTimerVersion) clearStopEtas();
        },
        Math.min(remaining, 2147483647),
      );
    }
    cleanup.push(() => {
      clearTimeout(etaExpiry);
    });
    cleanup.push(() => {
      currentPlan = null;
      currentProgress = null;
      etaSnapshot = null;
    });

    return {
      setInspectionSuspended(value) {
        if (disposed) return;
        inspector.setSuspended(value === true);
        if (value) {
          route.closePopup();
          stations.closePopup();
          nextLoads.clearSelection();
        }
      },
      setInspectorMode(kind, truckId = inspectionTruckId) {
        if (disposed || !['truck', 'next-stop', 'closed'].includes(kind))
          return;
        inspectionTruckId = truckId;
        inspector.setMode(kind, true);
        route.closePopup();
        stations.closePopup();
        if (kind !== 'next-stop') nextLoads.clearSelection();
      },
      showRoute(truckId) {
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
        cancelFuelFocus();
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
      setFuelEditorTruck(truckId) {
        if (
          disposed ||
          (truckId != null &&
            (truckId !== currentPlan?.truckId ||
              currentPlan?.tracking?.allStopsPassed))
        )
          return;
        if (truckId != null) cancelFuelFocus();
        trucks.setEditingTruck(truckId);
      },
      setRouteEditor(bytes) {
        if (disposed) return;
        const payload = bytes
          ? JSON.parse(new TextDecoder().decode(bytes))
          : null;
        routeEditor.set(payload);
        if (routeEditor.active) cancelFuelFocus();
        gpuScene.setRouteEditing?.(routeEditor.active);
        trucks.setEditingTruck(routeEditor.truckId);
      },
      closeStationPopup(captureEditorFocus = false) {
        if (disposed) return;
        if (captureEditorFocus) {
          cancelFuelReturn();
          fuelEditorOpener = element.ownerDocument?.activeElement;
        }
        stations.closePopup();
      },
      focusFuelStation(station) {
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
          cancelFuelFocus();
          return false;
        }
        trucks.setFollow(currentPlan.truckId, false);
        route.closePopup();
        stations.closePopup();
        nextLoads.clearSelection();
        focusFuelPosition(position, station);
        return true;
      },
      clearFuelStationFocus(restoreFocus = false, returnToRoute = null) {
        if (disposed) return;
        fuelEditing = false;
        cancelFuelFocus();
        stations.setEditing(null);
        if (restoreFocus) restoreFuelEditorFocus();
        if (returnToRoute) returnToFuelRoute(returnToRoute);
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
      setLoadReference(payload) {
        if (!disposed) route.setLoadReference(payload);
      },
      setDistanceUnit(value) {
        if (disposed) return;
        const next =
          value === 'miles' || value === 'kilometers' ? value : 'both';
        if (next === distanceUnit) return;
        distanceUnit = next;
        route.refreshDistances();
        stations.refreshDistances();
      },
      setStopEtas(payload) {
        if (disposed) return;
        if (!payload) {
          clearStopEtas();
          return;
        }
        if (
          payload.planId !== currentPlan?.id ||
          payload.planVersion !== currentPlan?.version ||
          payload.truckId !== currentPlan?.truckId ||
          payload.currentDispatchId !== currentPlan?.dispatchId ||
          (payload.currentExecutionLegId ?? null) !==
            (currentPlan?.executionLegId ?? null) ||
          (payload.currentAssignmentRevision ?? 0) !==
            (currentPlan?.assignmentRevision ?? 0)
        )
          return;
        const active = new Set(etaStops(currentPlan).map(stop => stop.id));
        const currentStops = (payload.eta?.stops || []).filter(
          stop =>
            stop.dispatchId === payload.currentDispatchId &&
            active.has(stop.stopId),
        );
        const pending =
          payload.refreshing === true ||
          payload.eta?.routeUpdatePending === true;
        const etaLabels = stopEtaLabels({
          ...payload.eta,
          stops: currentStops,
          routeUpdatePending: pending,
        });
        const currentLabels = new Map();
        for (const stop of currentStops) {
          const label = etaLabels.get(`${stop.dispatchId}:${stop.stopId}`);
          if (label) currentLabels.set(stop.stopId, label);
        }
        const fingerprint = JSON.stringify([
          payload.eta?.calculatedAt,
          payload.eta?.validUntil,
          currentStops,
          payload.eta?.cycleAtCalculation,
        ]);
        const complete =
          currentLabels.size > 0 &&
          (!etaRefreshing ||
            !etaSnapshot ||
            [...etaSnapshot.labels.keys()].every(id => currentLabels.has(id)));
        const calculatedAt = Date.parse(payload.eta?.calculatedAt ?? '');
        if (
          !pending &&
          currentLabels.size > 0 &&
          calculatedAt < etaSnapshot?.calculatedAt
        )
          return;
        const capture = () => ({
          labels: currentLabels,
          context: stopEtaIdentity(currentPlan),
          fingerprint,
          calculatedAt,
          validUntil: Date.parse(payload.eta.validUntil),
          graceUntil: stopEtaDeadline({
            ...payload.eta,
            routeUpdatePending: true,
          }),
        });
        if (
          !pending &&
          complete &&
          Date.parse(payload.eta.validUntil) > Date.now()
        ) {
          if (etaSnapshot?.fingerprint !== fingerprint) etaSnapshot = capture();
          etaRefreshing = false;
        } else if (pending || (etaRefreshing && !payload.eta)) {
          etaRefreshing = true;
          // Pending data cannot replace the deadline or recap/status rows.
          if (!etaSnapshot && currentLabels.size > 0) etaSnapshot = capture();
        } else {
          clearStopEtas();
          return;
        }
        showStopEtas();
      },
      setNextLoadsVisible(visible) {
        if (disposed) return;
        nextLoadsVisible = visible === true;
        nextLoads.setVisible(nextLoadsVisible);
        return refreshFuelRecommendations();
      },
      setNextLoadsBytes(bytes) {
        if (!disposed) {
          const payload = JSON.parse(new TextDecoder().decode(bytes));
          if (Array.isArray(payload)) nextLoads.set(payload);
          else {
            if (payload.truckId && payload.currentDispatchId) {
              if (
                nextLoadIdentity &&
                (payload.truckId !== nextLoadIdentity.truckId ||
                  payload.currentDispatchId !==
                    nextLoadIdentity.currentDispatchId ||
                  (payload.currentExecutionLegId ?? null) !==
                    (nextLoadIdentity.currentExecutionLegId ?? null) ||
                  (payload.currentAssignmentRevision ?? 0) !==
                    (nextLoadIdentity.currentAssignmentRevision ?? 0))
              )
                nextLoads.clear();
              nextLoadIdentity = {
                truckId: payload.truckId,
                currentDispatchId: payload.currentDispatchId,
                currentExecutionLegId: payload.currentExecutionLegId ?? null,
                currentAssignmentRevision:
                  payload.currentAssignmentRevision ?? 0,
              };
            }
            if (payload.routes) nextLoads.set(payload.routes);
          }
        }
      },
      /**
       * @param {Uint8Array} bytes
       * @param {import('./contracts.d.ts').RouteProgress | null} progress
       * @param {boolean} fit
       * @returns {Promise<boolean>}
       */
      async setRouteBytes(bytes, progress, fit) {
        return this.setRoute(
          JSON.parse(new TextDecoder().decode(bytes)),
          progress,
          fit,
        );
      },
      /**
       * @param {import('./contracts.d.ts').RoutePayload} payload
       * @param {import('./contracts.d.ts').RouteProgress | null} progress
       * @param {boolean} fit
       * @returns {Promise<boolean>}
       */
      async setRoute(payload, progress, fit) {
        if (disposed) return false;
        const merged = mergeRoutePayload(currentPlan, payload);
        if (!merged.accepted) return false;
        const version = ++routeVersion;
        await yieldToBrowser();
        if (version !== routeVersion || disposed) return false;
        const plan = merged.plan;
        if (!disposed) {
          if (etaSnapshot && etaSnapshot.context !== stopEtaIdentity(plan))
            clearStopEtas();
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
            cancelFuelFocus();
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
          if (etaSnapshot) showStopEtas();
          nextLoads.setStopOffset(orderedStops(plan).length);
          route.setRenderedPosition(
            plan?.truckId,
            trucks.getPosition(plan?.truckId),
            true,
          );
          await refreshFuelRecommendations();
          if (
            !disposed &&
            version === routeVersion &&
            Number.isFinite(progress?.progressMiles)
          )
            stations.setProgress(progress.progressMiles);
        }
        return !disposed && version === routeVersion;
      },
      clearSelection() {
        if (disposed) return;
        routeEditor.set(null);
        gpuScene.setRouteEditing?.(false);
        inspectionTruckId = null;
        inspector.setMode('closed');
        clearStopEtas();
        routeVersion++;
        currentPlan = null;
        fuelEditing = false;
        trucks.setEditingTruck(null);
        cancelFuelFocus();
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
      setTrucks(data, points) {
        if (!disposed) trucks.setTrucks(data, points);
      },
      focusTruck(id, zoom, preserveUserCamera) {
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
      setFollow(id, enabled) {
        if (!disposed) cameraViewport.refresh();
        return !disposed && trucks.setFollow(id, enabled);
      },
      setStations(data, date, useIfta) {
        if (!disposed) return stations.setStations(data, date, useIfta);
      },
      setPriceOverview(data, date, useIfta) {
        if (!disposed) return stations.setPriceOverview(data, date, useIfta);
      },
      setStationsVisible(value) {
        if (!disposed) return stations.setVisible(value);
      },
      setIfta(value) {
        if (!disposed) return stations.setIfta(value);
      },
      setTrafficVisible(value) {
        if (!disposed) traffic.setMap(value ? map : null);
      },
      async setOptions(options) {
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
