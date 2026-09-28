import { clusterCamera } from './truckClusters.ts';

// What deck.gl hands a hover or a click: the row of data under the pointer.
export type Pick = { object?: any };

/**
 * The pointer over the scene: what it is on, and what happens when it is
 * pressed. A mark answers for itself - a stop opens its card, a truck is
 * selected - so this only decides which mark the pointer means, keeps the
 * cursor honest about whether there is anything under it, and remembers
 * that a mark took the click so the map does not also take it.
 */
export function createScenePointer(
  map: google.maps.Map,
  {
    disposed,
    redraw,
    stationsVisible,
    selectStationById,
    clusterPadding,
  }: {
    disposed: () => boolean;
    redraw: () => void;
    // Away from the fuel view only a planned or edited station answers.
    stationsVisible: () => boolean;
    selectStationById: (id: string) => void;
    // What the camera must leave clear when a cluster is opened.
    clusterPadding: () => number | undefined;
  },
) {
  let hovered: any = null;
  let hoveredTruck: string | null = null;
  let clickAt = -Infinity;

  function setHover(info: Pick) {
    if (disposed()) return;
    const next = info.object || null;
    if (hovered?.onHover !== next?.onHover) {
      hovered?.onHover?.(false);
      next?.onHover?.(true);
    }
    if (!!hovered !== !!next)
      map.setOptions({ draggableCursor: next ? 'pointer' : 'default' });
    hovered = next;
  }

  const took = () => {
    clickAt = performance.now();
  };

  return {
    setHover,
    hoveredTruck: () => hoveredTruck,
    // A mark that is going away takes its hover with it.
    hoverEnded(onHover: unknown) {
      if (onHover && hovered?.onHover === onHover) setHover({ object: null });
    },
    clear() {
      hovered = null;
      hoveredTruck = null;
    },
    took,
    // Whether a mark has just taken a click, so the map's own click on the
    // same press is not a click on nothing.
    tookRecently: () => performance.now() - clickAt < 100,
    hoverTruck(info: Pick) {
      setHover(info);
      const next = info.object?.unit ?? null;
      if (next === hoveredTruck) return;
      hoveredTruck = next;
      redraw();
    },
    selectStation(info: Pick) {
      if (
        disposed() ||
        !info.object ||
        (!stationsVisible() && !info.object.editing && !info.object.recommended)
      )
        return false;
      took();
      selectStationById(info.object.id);
      return true;
    },
    selectTruck(info: Pick) {
      if (disposed() || !info.object) return false;
      took();
      info.object.onSelect();
      return true;
    },
    selectStop(info: Pick) {
      if (disposed() || !info.object) return false;
      took();
      info.object.onSelect?.();
      return true;
    },
    // A cluster is not opened by selecting anything: the camera moves to
    // where the trucks it stands for come apart.
    selectCluster(info: Pick) {
      if (disposed() || !info.object) return false;
      took();
      const element = map.getDiv?.();
      const camera = clusterCamera(
        info.object.members,
        element?.clientWidth,
        element?.clientHeight,
        clusterPadding(),
      );
      if (camera) glide(map, camera);
      return true;
    },
  };
}

// The camera eased to where a cluster comes apart instead of jumping there
// (the owner, September 27): the centre and zoom travel together over a
// short ease-in-out; a drag or wheel meanwhile takes the camera back.
function glide(
  map: google.maps.Map,
  camera: google.maps.CameraOptions,
  duration = 700,
) {
  const from = map.getCenter?.();
  const fromZoom = map.getZoom?.();
  const to = camera.center as google.maps.LatLngLiteral | undefined;
  const toZoom = camera.zoom;
  const frame = globalThis.requestAnimationFrame;
  if (
    !from ||
    !to ||
    !Number.isFinite(fromZoom) ||
    !Number.isFinite(toZoom) ||
    typeof frame !== 'function'
  ) {
    map.moveCamera(camera);
    return;
  }
  const start = { lat: from.lat(), lng: from.lng(), zoom: fromZoom! };
  const end = {
    lat: typeof to.lat === 'function' ? (to as any).lat() : to.lat,
    lng: typeof to.lng === 'function' ? (to as any).lng() : to.lng,
    zoom: toZoom!,
  };
  let stopped = false;
  const stops = ['dragstart', 'mousedown', 'wheel'].map(name =>
    map.addListener?.(name, () => {
      stopped = true;
    }),
  );
  const begun = performance.now();
  const step = (now: number) => {
    if (stopped) {
      for (const listener of stops) listener?.remove?.();
      return;
    }
    const t = Math.min(1, (now - begun) / duration);
    const e = t < 0.5 ? 2 * t * t : 1 - (-2 * t + 2) ** 2 / 2;
    map.moveCamera({
      center: {
        lat: start.lat + (end.lat - start.lat) * e,
        lng: start.lng + (end.lng - start.lng) * e,
      },
      zoom: start.zoom + (end.zoom - start.zoom) * e,
    });
    if (t < 1) frame(step);
    else for (const listener of stops) listener?.remove?.();
  };
  frame(step);
}
