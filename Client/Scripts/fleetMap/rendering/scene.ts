import type { DeckLayerFactory } from './deckLayer.ts';
import type { StopLabelStyle } from './stopLabelStyle.ts';
import { releaseAll } from '../lifecycle/release.ts';
import { createSceneMarks } from './sceneMarks.ts';
import { createSceneLayers } from './sceneLayers.ts';
import { createSceneOverlay } from './sceneOverlay.ts';
import { createScenePointer } from './scenePointer.ts';
import { snapshotStops } from './stopData.ts';
import { clusterTrucks } from './truckClusters.ts';
import { layoutMapLabels } from './truckLabelLayout.ts';

// What deck.gl hands a hover or a click: the row of data under the pointer.
type Pick = { object?: any };

export function createScene(
  map: google.maps.Map,
  {
    GoogleMapsOverlay,
    ScatterplotLayer,
    PathLayer,
    IconLayer,
    TextLayer,
    routeDashExtensions,
  }: {
    GoogleMapsOverlay: new (props: Record<string, any>) => any;
    ScatterplotLayer: DeckLayerFactory;
    PathLayer: DeckLayerFactory;
    IconLayer: DeckLayerFactory;
    TextLayer: DeckLayerFactory;
    routeDashExtensions?: unknown;
  },
) {
  const stations = new Map<string, any>(),
    trucks = new Set<any>(),
    lines = new Set<any>();
  const stops = new Set<any>();
  let stationsVisible = false,
    stationData: any[] = [],
    stationDirty = true;
  let stopsDirty = true,
    stopData: any[] = [],
    distanceData: any[] = [];
  let vehiclesDirty = true,
    vehicles: any[] = [],
    standingTrucks = '';
  let vehicleDisplay: { vehicles: any[]; clusters: any[] } = {
      vehicles: [],
      clusters: [],
    },
    clusterZoom = map.getZoom?.() ?? 12;
  // Badges are laid out against the screen, so a new zoom is a new layout -
  // but a zoom gesture reports a new zoom on every frame of itself, and
  // relaying every badge on each of them made the map crawl. Whole zoom
  // levels are a factor of two apart; nothing about the layout turns on
  // less than that.
  let stopZoom = Math.round(clusterZoom);
  const clusterZoomListener = map.addListener('zoom_changed', () => {
    const next = map.getZoom?.() ?? 12;
    if (next === clusterZoom) return;
    clusterZoom = next;
    if (Math.round(next) !== stopZoom) {
      stopZoom = Math.round(next);
      stopsDirty = true;
    }
    invalidateVehicles();
  });
  const buildLayers = createSceneLayers({
    ScatterplotLayer,
    PathLayer,
    IconLayer,
    TextLayer,
    routeDashExtensions,
  });
  let frame: number | null = null,
    disposed = false;
  let stationSelect: (stationId: string) => void = () => {};
  // Asked what padding the camera should leave when a cluster is opened.
  let clusterSelect: () => number | undefined = () => undefined;
  let routeEditing = false;
  const overlay = createSceneOverlay(map, GoogleMapsOverlay, {
    onChange: () => schedule(),
    // A click on nothing looks for a station near it, unless a road is
    // being chosen or there are no stations to find.
    canPick: () =>
      !routeEditing &&
      (stationsVisible ||
        stationData.some(station => station.editing || station.recommended)),
    onPick: nearby => selectStation(nearby),
  });

  function schedule() {
    if (frame !== null || disposed) return;
    frame = requestAnimationFrame(render);
  }
  function render() {
    frame = null;
    if (disposed) return;
    if (stationDirty) {
      stationData = [...stations.values()];
      stationDirty = false;
    }
    const previousStops = stopData;
    if (vehiclesDirty) {
      vehicles = [...trucks].filter(t => t.visible && t.position);
      // A truck standing on a stop is drawn as the ring around that stop's
      // badge, so which trucks are standing and where is part of what the
      // stops are laid out against. Without this a badge kept a ring for a
      // truck that had since driven off, until something else happened to
      // move the stops. A truck in motion is not in the key, so driving
      // relays nothing.
      const standing = vehicles
        .filter(t => !(t.speed > 0))
        .map(t => t.position.join(','))
        .join(';');
      if (standing !== standingTrucks) {
        standingTrucks = standing;
        stopsDirty = true;
      }
    }
    if (stopsDirty) {
      ({ stopData, distanceData } = snapshotStops(
        routeEditing
          ? [...stops].filter(stop => stop.routeRole === 'preview')
          : stops,
        stopData,
        distanceData,
        stopZoom,
        vehicles,
      ));
      stopsDirty = false;
    }
    // Labels step aside from stops, so stops that moved move labels: a route
    // that arrives after the trucks did used to leave them where they were.
    // Row identity is the test, not the dirty flag - a distance label that
    // changed must not rebuild the truck layers.
    const stopsMoved =
      stopData.length !== previousStops.length ||
      stopData.some((row, index) => row !== previousStops[index]);
    if (vehiclesDirty || stopsMoved) {
      const grouped = clusterTrucks(vehicles, Math.floor(clusterZoom));
      vehicleDisplay = layoutMapLabels({
        vehicles: grouped.vehicles,
        clusters: grouped.clusters,
        zoom: clusterZoom,
        previous: vehicleDisplay.vehicles,
      });
      vehiclesDirty = false;
    }
    overlay.draw(
      buildLayers({
        lines: routeEditing
          ? [...lines].filter(line => line.routeRole === 'preview')
          : lines,
        stationData: routeEditing ? [] : stationData,
        stationsVisible,
        stopData,
        distanceData,
        vehicles: vehicleDisplay.vehicles,
        clusters: vehicleDisplay.clusters,
        hasSelectedTruck: vehicles.some(truck => truck.selected),
        selectCluster: pointer.selectCluster,
        hoveredTruck: pointer.hoveredTruck(),
        setHover,
        selectStop: pointer.selectStop,
        hoverTruck: pointer.hoverTruck,
        selectTruck: pointer.selectTruck,
        selectStation,
        pixelRatio: overlay.pixelRatio(),
        zoom: clusterZoom,
        stopLabelStyle: overlay.stopLabelStyle(),
      }),
    );
  }
  const pointer = createScenePointer(map, {
    disposed: () => disposed,
    redraw: () => schedule(),
    stationsVisible: () => stationsVisible,
    selectStationById: id => stationSelect(id),
    clusterPadding: () => clusterSelect(),
  });
  const { setHover, selectStation } = pointer;
  function invalidateStops() {
    stopsDirty = true;
    schedule();
  }
  function invalidateVehicles() {
    vehiclesDirty = true;
    schedule();
  }
  // Everything the map holds a handle to - a road, a stop's badge, a truck,
  // the stations - records itself here and says what must be drawn again.
  const marks = createSceneMarks({
    lines,
    stops,
    trucks,
    stations,
    disposed: () => disposed,
    editing: () => routeEditing,
    stopsChanged: invalidateStops,
    vehiclesChanged: invalidateVehicles,
    stationsChanged: () => {
      stationDirty = true;
    },
    redraw: schedule,
    clicked: pointer.took,
    hoverEnded: pointer.hoverEnded,
    showStations: value => {
      if (stationsVisible === value) return;
      stationsVisible = value;
      vehiclesDirty = true;
      schedule();
    },
    stationsSelectedBy: select => {
      stationSelect = select;
    },
  });
  return {
    ...marks,
    setClusterSelect(callback: () => number | undefined) {
      clusterSelect = callback;
    },
    setRouteEditing(value: boolean) {
      if (routeEditing === value) return;
      routeEditing = value;
      // Removed Deck layers are finalized; keep geometry, not their renderer instances.
      for (const line of lines) line.cachedLayer = null;
      setHover({ object: null });
      invalidateStops();
    },
    consumeTruckClick() {
      return pointer.tookRecently();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      // The overlay holds a WebGL context and the observers hold this whole
      // closure, so every one of these has to run even if an earlier one
      // throws.
      releaseAll([
        () => {
          if (frame !== null) cancelAnimationFrame(frame);
          frame = null;
        },
        () => {
          stationSelect = () => {};
          clusterSelect = () => undefined;
          pointer.clear();
          stationData = [];
          stopData = [];
          distanceData = [];
          vehicles = [];
        },
        () => clusterZoomListener.remove(),
        () => stops.clear(),
        ...overlay.release,
        () => stations.clear(),
        () => trucks.clear(),
        () => lines.clear(),
      ]);
    },
  };
}
