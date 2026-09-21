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
      if (camera) map.moveCamera(camera);
      return true;
    },
  };
}
