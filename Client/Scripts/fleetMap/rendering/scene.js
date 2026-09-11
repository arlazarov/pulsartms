import { createSceneLayers } from './sceneLayers.js';
import { snapshotStops } from './stopData.js';
import { pickNearbyStation } from './stationTouch.js';
import { readStopLabelStyle } from './stopLabelStyle.js';

const xy = (p) => [
  typeof p.lng === 'function' ? p.lng() : p.lng,
  typeof p.lat === 'function' ? p.lat() : p.lat,
];
const rgb = (c) =>
  c
    .replace(/^rgb\(|\)$/g, '')
    .split(',')
    .map(Number);

export function createScene(map, { GoogleMapsOverlay, ScatterplotLayer, PathLayer, IconLayer, TextLayer, routeDashExtensions }) {
  const stations = new Map(),
    trucks = new Set(),
    lines = new Set();
  const stops = new Set();
  let stationsVisible = false,
    stationData = [],
    stationDirty = true;
  let stopsDirty = true,
    stopData = [],
    distanceData = [];
  let vehiclesDirty = true,
    vehicles = [];
  const buildLayers = createSceneLayers({ ScatterplotLayer, PathLayer, IconLayer, TextLayer, routeDashExtensions });
  const viewport = map.getDiv().ownerDocument?.defaultView;
  let stopLabelStyle = readStopLabelStyle(map.getDiv());
  const themeObserver = viewport?.MutationObserver ? new viewport.MutationObserver(() => {
    stopLabelStyle = readStopLabelStyle(map.getDiv());
    schedule();
  }) : null;
  for (const element of [map.getDiv().ownerDocument?.documentElement, map.getDiv().ownerDocument?.body])
    if (element) themeObserver?.observe(element, { attributes: true, attributeFilter: ['data-theme', 'class', 'style'] });
  let pixelRatio = viewport?.devicePixelRatio || 1;
  let frame = null,
    disposed = false,
    hovered = null,
    truckClickAt = -Infinity;
  let stationSelect = () => {};
  let hoveredTruck = null;
  // All fleet layers share one foreground canvas, with explicit drawing order.
  // On vector maps this overlay receives the same onDraw camera transformer.
  const truckOverlay = new GoogleMapsOverlay({
    id: 'fleet-top-layer',
    interleaved: false,
    useDevicePixels: true,
    style: { top: '0', left: '0', width: '100%', height: '100%', zIndex: '1', pointerEvents: 'none' },
    layers: [],
    onClick: (info, event) => {
      if (disposed || !stationsVisible && !stationData.some(station => station.editing || station.recommended)) return;
      const nearby = pickNearbyStation(truckOverlay, info, event);
      if (nearby?.object) selectStation(nearby);
    },
  });
  // GoogleMapsOverlay owns camera synchronization; zoom does not change data.
  truckOverlay.setMap(map);
  const densityChanged = () => {
    const next = viewport.devicePixelRatio || 1;
    if (next === pixelRatio) return;
    pixelRatio = next;
    schedule();
  };
  viewport?.addEventListener('resize', densityChanged);
  map.getDiv().dataset.renderer = 'gpu';
  const modeListener = map.addListener('renderingtype_changed', () => {
    const mode = map.getRenderingType();
    map.getDiv().dataset.renderer = `gpu-${mode}`;
    if (mode !== 'VECTOR') console.warn(`[Fleet map] Vector renderer unavailable: ${mode}`);
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
    if (stopsDirty) {
      ({ stopData, distanceData } = snapshotStops(stops, stopData, distanceData));
      stopsDirty = false;
    }
    if (vehiclesDirty) {
      vehicles = [...trucks].filter((t) => t.visible && t.position);
      vehiclesDirty = false;
    }
    truckOverlay.setProps({
      layers: buildLayers({
        lines,
        stationData,
        stationsVisible,
        stopData,
        distanceData,
        vehicles,
        hoveredTruck,
        setHover,
        selectStop,
        hoverTruck,
        selectTruck,
        selectStation,
        pixelRatio,
        stopLabelStyle,
      }),
    });
  }
  function setHover(info) {
    if (disposed) return;
    const next = info.object || null;
    if (hovered?.onHover !== next?.onHover) {
      hovered?.onHover?.(false);
      next?.onHover?.(true);
    }
    if (!!hovered !== !!next) map.setOptions({ draggableCursor: next ? 'pointer' : 'default' });
    hovered = next;
  }
  function hoverTruck(info) {
    setHover(info);
    const next = info.object?.unit ?? null;
    if (next === hoveredTruck) return;
    hoveredTruck = next;
    schedule();
  }
  function selectStation(info) {
    if (disposed || !info.object || !stationsVisible && !info.object.editing && !info.object.recommended) return false;
    truckClickAt = performance.now();
    stationSelect(info.object.id);
    return true;
  }
  function selectTruck(info) {
    if (disposed || !info.object) return false;
    truckClickAt = performance.now();
    info.object.onSelect();
    return true;
  }
  let lineId = 0, stopId = 0;
  function selectStop(info) {
    if (disposed || !info.object) return false;
    truckClickAt = performance.now();
    info.object.onSelect?.();
    return true;
  }
  function invalidateStops() {
    stopsDirty = true;
    schedule();
  }
  function invalidateVehicles() {
    vehiclesDirty = true;
    schedule();
  }
  class Polyline {
    constructor(options) {
      Object.assign(this, options);
      this.id = `route-${++lineId}`;
      this.path = [];
      this.data = [this.path];
      lines.add(this);
      schedule();
    }
    setOptions(options) {
      Object.assign(this, options);
      schedule();
    }
    setMap(value) {
      this.map = value;
      if (value) lines.add(this);
      else lines.delete(this);
      schedule();
    }
    setPath(path) {
      this.path = path.map(xy);
      this.data = [this.path];
      schedule();
    }
    getPath() {
      return {
        removeAt: (i) => {
          this.path = this.path.filter((_, index) => index !== i);
          this.data = [this.path];
          schedule();
        },
        setAt: (i, p) => {
          this.path = this.path.slice();
          this.path[i] = xy(p);
          this.data = [this.path];
          schedule();
        },
      };
    }
  }
  return {
    Polyline,
    StopMarker: class {
      constructor(options) {
        this.id = ++stopId;
        this.position = xy(options.position);
        this.number = options.number;
        this.color = options.color;
        this.onSelect = options.onSelect;
        this.onHover = options.onHover;
        this.transientLabel = options.transientLabel;
        this.job = options.job;
        this.distance = null;
        this.distanceTones = [];
        stops.add(this);
        invalidateStops();
      }
      setDistance(value, tones = []) {
        if (this.distance === value && this.distanceTones.length === tones.length
          && tones.every((tone, index) => tone === this.distanceTones[index])) return;
        this.distance = value;
        this.distanceTones = tones;
        invalidateStops();
      }
      setVisible(value) {
        if (this.visible === value) return;
        this.visible = value;
        if (!value && hovered?.onHover === this.onHover) setHover({ object: null });
        invalidateStops();
      }
      setNumber(value) {
        if (this.number === value) return;
        this.number = value;
        invalidateStops();
      }
      setJob(value) {
        if (this.job === value) return;
        this.job = value;
        invalidateStops();
      }
      get highlighted() { return this._highlighted === true; }
      set highlighted(value) {
        if (this.highlighted === value) return;
        this._highlighted = value;
        invalidateStops();
      }
      set map(value) {
        if (!value) {
          if (hovered?.onHover && hovered.onHover === this.onHover) setHover({ object: null });
          this.onHover = null;
          this.onSelect = null;
          stops.delete(this);
          invalidateStops();
        }
      }
    },
    consumeTruckClick() {
      return performance.now() - truckClickAt < 100;
    },
    createTruckMarker(_, onSelect) {
      const t = { onSelect, visible: true, unit: '', engine: '', position: null, heading: 0, speed: 0 };
      trucks.add(t);
      return {
        update(value) {
          const unit = value.unitNumber || '',
            engine = value.engineState?.toLowerCase() || '';
          if (t.unit === unit && t.engine === engine) return;
          t.unit = unit;
          t.engine = engine;
          invalidateVehicles();
        },
        render(p) {
          if (
            !p ||
            (t.position?.[0] === p.longitude &&
              t.position?.[1] === p.latitude &&
              t.heading === (p.heading || 0) &&
              t.speed === (p.speed || 0))
          )
            return;
          t.position = [p.longitude, p.latitude];
          t.heading = p.heading || 0;
          t.speed = p.speed || 0;
          invalidateVehicles();
        },
        setVisible(value) {
          if (t.visible === value) return;
          t.visible = value;
          invalidateVehicles();
        },
        setSelected(value) {
          if (t.selected === value) return;
          t.selected = value;
          invalidateVehicles();
        },
        dispose() {
          trucks.delete(t);
          invalidateVehicles();
        },
      };
    },
    createStationPointLayer(_, onSelect) {
      stationSelect = onSelect;
      return {
        setPoint(id, p, c, recommended, selected, numbers = '', editing = false, price = null) {
          const old = stations.get(id);
          if (
            old &&
            old.position[0] === p.lng &&
            old.position[1] === p.lat &&
            old.sourceColor === c &&
            old.recommended === recommended &&
            old.selected === selected && old.numbers === numbers && old.editing === editing && old.price === price
          )
            return;
          stations.set(id, {
            id,
            position: [p.lng, p.lat],
            color: rgb(c),
            sourceColor: c,
            recommended,
            selected,
            numbers,
            editing,
            price,
          });
          stationDirty = true;
        },
        removePoint(id) {
          if (stations.delete(id)) stationDirty = true;
        },
        redraw: schedule,
        setVisible(value) {
          if (stationsVisible === value) return;
          stationsVisible = value;
          schedule();
        },
        hitTest() {
          return null;
        },
        dispose() {
          stations.clear();
          stationDirty = true;
          schedule();
        },
      };
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      if (frame !== null) cancelAnimationFrame(frame);
      frame = null;
      stationSelect = () => {};
      hovered = null;
      hoveredTruck = null;
      stationData = []; stopData = []; distanceData = []; vehicles = [];
      modeListener.remove();
      themeObserver?.disconnect();
      viewport?.removeEventListener('resize', densityChanged);
      stops.clear();
      truckOverlay.finalize();
      stations.clear();
      trucks.clear();
      lines.clear();
    },
  };
}
