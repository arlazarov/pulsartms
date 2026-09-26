import { createDockedDetails } from '../../Scripts/fleetMap/ui/dockedDetails.ts';
import { createRouteStops } from '../../Scripts/fleetMap/routes/routeStops.ts';
import { createStopEtaWindow } from '../../Scripts/fleetMap/routes/stopEtaWindow.ts';
import { createStationLayer } from '../../Scripts/fleetMap/stations/stationLayer.ts';
import { fuelRecommendations } from '../../Scripts/fleetMap/stations/fuelRecommendations.ts';
import { mergeRoutePayload } from '../../Scripts/fleetMap/routes/routePayload.ts';
import { parseRoutePayload } from '../../Scripts/fleetMap/geometry/encodedPath.ts';
import { distanceLabel } from '../../Scripts/fleetMap/ui/distanceLabel.ts';

// The Fleet Map module as the design probe loads it: the production docked
// inspector, route stop cards, arrival window and station layer and cards,
// wired the way fleetMap.ts wires them. Only the provider map, the GPU
// markers and the camera are absent, so a marker is "clicked" by name.
export async function createFleetMap(element, _key, callbacks) {
  element.style.background = 'var(--ui-surface-muted)';
  let distanceUnit = 'both';
  const formatDistance = miles => distanceLabel(miles, distanceUnit);
  let inspectionTruckId = null;
  let currentPlan = null;
  let currentProgress = null;
  const notify = (method, ...args) =>
    callbacks?.invokeMethodAsync(method, ...args).catch(() => {});
  const inspector = createDockedDetails(
    element.parentElement?.querySelector('.fleet-map-inspector__native'),
    (kind, revision) => {
      window.designFixture?.requests.push(`mode:${kind}`);
      return notify('OnMapInspectorChanged', kind, inspectionTruckId, revision);
    },
    () => (inspectionTruckId ? 'truck' : 'closed'),
    element,
  );
  const markers = new Map();
  class StopMarker {
    constructor(options) {
      Object.assign(this, options);
      markers.set(`${options.number}`, this);
    }
    setNumber(number) {
      markers.set(`${number}`, this);
    }
    setJob() {}
    setDone() {}
  }
  const popup = inspector.popupFactory('stop')(null, {
    onClose: () => stops.close(),
  });
  const stops = createRouteStops(
    null,
    StopMarker,
    popup,
    () => {
      inspector.activate('stop');
      stations.closePopup();
    },
    formatDistance,
  );
  const etas = createStopEtaWindow(
    labels => stops.setEtas(labels),
    () => false,
  );
  let selectStation = () => {};
  const stations = createStationLayer(
    null,
    () => {
      inspector.activate('fuel');
      stops.close();
    },
    (_map, onSelect) => {
      selectStation = onSelect;
      return {
        setPoint() {},
        removePoint() {},
        redraw() {},
        setVisible() {},
        hitTest: () => null,
        dispose() {},
      };
    },
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
  window.designFixture = {
    selectTruck: id => notify('OnTruckSelected', id),
    openStop: number => markers.get(`${number}`)?.onSelect(),
    openStation: id => selectStation(id),
    // As the next-loads layer reports a picked badge: with the current
    // work's execution leg and assignment revision, which the page checks
    // against what it has selected.
    selectNextStop: (truck, current, load, index, revision) =>
      notify(
        'OnNextExecutionLegSelected',
        truck,
        current,
        load,
        index,
        null,
        null,
        revision,
      ),
    requests: [],
  };
  const record =
    name =>
    (...args) => {
      window.designFixture.requests.push(name);
      return args;
    };
  return {
    setOptions: record('setOptions'),
    setTrucks: record('setTrucks'),
    setTrafficVisible: record('setTrafficVisible'),
    setIfta: value => stations.setIfta(value),
    setStationsVisible: value => stations.setVisible(value),
    setStations: (data, date, ifta) => stations.setStations(data, date, ifta),
    setPriceOverview: (data, date, ifta) =>
      stations.setPriceOverview(data, date, ifta),
    clearSelection() {
      inspectionTruckId = null;
      inspector.setMode('closed');
      currentPlan = null;
      stops.clear();
      stations.closePopup();
      stations.setEditContext(null);
      return stations.setRecommended([]);
    },
    clearNextLoads() {},
    setNextLoadsVisible() {},
    setNextLoadsBytes: record('setNextLoadsBytes'),
    clearNextLoadSelection() {},
    closeStationPopup: () => {
      window.designFixture.requests.push('closeStationPopup');
      stations.closePopup();
    },
    openStation: id => stations.openStation(id),
    setStopEtas: payload => etas.receive(payload, currentPlan),
    setLoadReference: value => stops.setLoadReference(value),
    setDistanceUnit(value) {
      distanceUnit =
        value === 'miles' || value === 'kilometers' ? value : 'both';
      stops.refreshDistances();
      stations.refreshDistances();
    },
    setFollow: () => true,
    finishInitialView() {},
    setInspectorMode(kind, truckId = inspectionTruckId) {
      window.designFixture.requests.push(`setInspectorMode:${kind}`);
      if (!['truck', 'next-stop', 'closed'].includes(kind)) return;
      inspectionTruckId = truckId;
      inspector.setMode(kind, true);
      stops.close();
      stations.closePopup();
    },
    clearMapInspection() {
      inspector.setMode('closed', true);
      stops.close();
      stations.closePopup();
    },
    setInspectionSuspended(value) {
      window.designFixture.requests.push(`suspended:${value}`);
      inspector.setSuspended(value === true);
    },
    setFuelEditorTruck() {},
    focusFuelStation: record('focusFuelStation'),
    clearFuelStationFocus() {},
    showRoute: record('showRoute'),
    focusTruck(id) {
      inspectionTruckId = id;
      inspector.setMode('truck');
      return true;
    },
    setRouteEditor() {},
    selectNextStop() {},
    async setRouteBytes(bytes, progress) {
      const merged = mergeRoutePayload(currentPlan, parseRoutePayload(bytes));
      if (!merged.accepted) return false;
      const plan = merged.plan;
      etas.forgetIfPlanChanged(plan);
      currentPlan = plan;
      currentProgress = progress;
      inspectionTruckId = plan?.truckId ?? inspectionTruckId;
      stations.setEditContext(
        plan?.truckId && plan?.dispatchId && !plan.tracking?.allStopsPassed
          ? { truckId: plan.truckId, dispatchId: plan.dispatchId }
          : null,
      );
      stops.setPlan(plan);
      stops.setProgress(progress?.progressMiles ?? null);
      await stations.setRecommended(fuelRecommendations(plan, progress).stops);
      if (Number.isFinite(progress?.progressMiles))
        stations.setProgress(progress.progressMiles);
      return true;
    },
    dispose() {
      inspector.dispose();
      delete window.designFixture;
    },
  };
}
