import { GoogleMapsOverlay } from '@deck.gl/google-maps';
import {
  ScatterplotLayer,
  PathLayer,
  IconLayer,
  TextLayer,
} from '@deck.gl/layers';
import { createScene } from '../../Scripts/fleetMap/rendering/scene.js';
import { createMapHost } from '../../Scripts/fleetMap/provider/mapHost.js';

const root = document.getElementById('map');
const overlays = new Set();
let pendingFrame = null,
  frameRequests = 0,
  cameraWrites = 0;
let holdCamera = false;
let mapCreations = 0,
  adapter,
  scene,
  mounted;
const point = (lng, lat) => ({ lng: () => lng, lat: () => lat });
const camera = { center: point(-96, 38), zoom: 5, tilt: 0, heading: 0 };

class NativeOverlay {
  setMap(map) {
    if (this.map === map) return;
    if (this.map) {
      overlays.delete(this);
      this.onRemove?.();
    }
    this.map = map;
    if (!map) return;
    overlays.add(this);
    this.onAdd?.();
    // A reattached, stationary map need not emit a camera frame on its own.
    queueMicrotask(() => {
      if (this.map) this.onContextRestored?.({ gl: null });
    });
  }
  getMap() {
    return this.map;
  }
  requestRedraw() {
    frameRequests++;
    if (holdCamera) return;
    paintCamera();
  }
}

function paintCamera() {
  if (pendingFrame !== null) return;
  pendingFrame = requestAnimationFrame(() => {
    pendingFrame = null;
    for (const overlay of overlays) {
      overlay.onDraw?.({ transformer: { getCameraParams: () => camera } });
    }
  });
}
window.google = {
  maps: {
    RenderingType: { VECTOR: 'VECTOR', UNINITIALIZED: 'UNINITIALIZED' },
    WebGLOverlayView: NativeOverlay,
    OverlayView: class {},
  },
};
class ObservedOverlay extends GoogleMapsOverlay {
  constructor(props) {
    super(props);
    adapter = this;
  }
}
const listeners = new Set();
const mountMap = createMapHost(host => {
  mapCreations++;
  const base = document.createElement('div');
  base.style.cssText = 'width:100%;height:100%';
  host.append(base);
  return {
    getDiv: () => host,
    getRenderingType: () => 'VECTOR',
    getZoom: () => camera.zoom,
    setOptions() {},
    moveCamera() {
      cameraWrites++;
    },
    addListener(name, callback) {
      const entry = { name, callback };
      listeners.add(entry);
      return { remove: () => listeners.delete(entry) };
    },
  };
});
const specs = [
  { unitNumber: '11006', longitude: -105, latitude: 35, speed: 40 },
  { unitNumber: '54777', longitude: -77, latitude: 43, speed: 0 },
  { unitNumber: '11007', longitude: -81, latitude: 27, speed: 40 },
  { unitNumber: '11005', longitude: -115, latitude: 35, speed: 0 },
];
window.remountFixture = {
  holdCamera(value) {
    holdCamera = value;
    if (!value) paintCamera();
  },
  open() {
    mounted = mountMap(root, {});
    scene = createScene(mounted.map, {
      GoogleMapsOverlay: ObservedOverlay,
      ScatterplotLayer,
      PathLayer,
      IconLayer,
      TextLayer,
    });
    for (const spec of specs) {
      const marker = scene.createTruckMarker(mounted.map, () => {});
      marker.update({ ...spec, engineState: 'on' });
      marker.render(spec);
    }
    mounted.initialCamera(() => {}, false);
  },
  close() {
    scene.dispose();
    mounted.release();
  },
  report() {
    const deck = adapter?._deck;
    const view = deck?.getViewports()[0];
    const layers = deck?.props.layers ?? [];
    const layer = layers.find(layer => layer.id === 'truck-icons');
    const position = spec => {
      const [x, y] = view.project([spec.longitude, spec.latitude]);
      return { unit: spec.unitNumber, x, y };
    };
    return {
      loaded: !!deck?.isInitialized && !!layer?.isLoaded,
      ready:
        !!deck?.isInitialized &&
        !!layer?.isLoaded &&
        layers.every(layer => layer.isLoaded) &&
        !deck.props.layerFilter,
      hidden: !!deck?.props.layerFilter,
      camera: view && {
        longitude: view.longitude,
        latitude: view.latitude,
        zoom: view.zoom,
      },
      trucks: view ? specs.map(position) : [],
      mapCreations,
      cameraWrites,
      frameRequests,
      overlays: overlays.size,
      listeners: listeners.size,
      canvases: root.querySelectorAll('canvas').length,
    };
  },
};
