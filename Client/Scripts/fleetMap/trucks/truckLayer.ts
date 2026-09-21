import type { RoutePoint } from '../contracts.d.ts';
import type { TruckPoint } from './truckPoints.ts';
import type { CameraViewport } from './truckCamera.ts';
import type { TruckMotion } from './truckMotion.ts';
import { createTruckPoint } from './truckPoints.ts';
import { createTruckCamera } from './truckCamera.ts';
import {
  advanceMotion,
  createTruckMotion,
  receivePoints,
  restartMotion,
} from './truckMotion.ts';
import { getTruckPosition, truckPlaybackDelay } from './truckPlayback.ts';

// One telemetry reading as the page sends it: which truck it belongs to and
// where that truck was.
export type TruckReading = {
  truckId: string;
  truckExternalId?: string;
  latitude?: number;
  longitude?: number;
  updatedAt?: string;
  speed?: number;
  heading?: number;
};

// What this layer asks of a truck's marker, whoever draws it.
export type TruckMarker = {
  render(position: RoutePoint): void;
  update(reading: TruckReading): void;
  setSelected(selected: boolean): void;
  setVisible(visible: boolean): void;
  dispose(): void;
};

// One truck as this layer holds it: which truck it is, the marker drawn for
// it, and its motion.
type Truck = TruckMotion & { id: string; marker: TruckMarker };

/**
 * Every truck on the map: where it is, where it is going, and which one the
 * page has open. The camera over them is its own thing - see truckCamera.
 *
 * @param onOpen Told which truck the page should open.
 * @param onPosition Told where a truck has been drawn.
 * @param displayPosition
 *   Where a truck should be drawn now - the playback may have it between
 *   two reported points. Positions cross the line the way the server says
 *   them, latitude and longitude, not the map's lat and lng.
 * @param onFollowChange Whether the camera is now following the open truck.
 * @param initialCamera
 *   Runs the first camera move, optionally once the map has settled; called
 *   with nothing when the user has taken the camera over.
 */
export function createTruckLayer(
  map: google.maps.Map,
  onOpen: (truckId: string | undefined) => void = () => {},
  onPosition: (truckId: string, position: RoutePoint | null) => void = () => {},
  displayPosition: (truckId: string, position: TruckPoint) => TruckPoint = (
    _,
    position,
  ) => position,
  markerFactory: (
    map: google.maps.Map,
    onSelect: () => void,
    onDismiss: () => void,
  ) => TruckMarker = () => {
    throw new Error('The truck layer was mounted without a marker.');
  },
  onFollowChange: (following: boolean) => void = () => {},
  initialCamera: (
    change?: (initial: boolean) => void,
    waitForIdle?: boolean,
  ) => void = (change, _waitForIdle) => change?.(true),
  cameraViewport?: CameraViewport,
) {
  const trucks = new Map<string, Truck>();
  let selectedId: string | null = null;
  let editingTruckId: string | null = null;
  const isVisible = (truck: Truck) =>
    editingTruckId === null || truck.id === editingTruckId;
  let frame: number | null = null;
  let disposed = false;

  const byTruckId = (id: string) =>
    [...trucks.values()].find(truck => truck.id === id) ?? null;
  const camera = createTruckCamera(map, {
    truckPosition: id => {
      const truck = byTruckId(id);
      return truck?.position ? displayPosition(truck.id, truck.position) : null;
    },
    onFollowChange,
    initialCamera,
    viewport: cameraViewport,
  });

  function select(id: string | null, notify = true) {
    if (disposed) return;
    if (id === selectedId) {
      if (id !== null && notify) onOpen(trucks.get(id)?.id);
      return;
    }
    if (selectedId !== null) trucks.get(selectedId)?.marker.setSelected(false);
    camera.endFollow();
    selectedId = id;
    if (id !== null) trucks.get(id)?.marker.setSelected(true);
    if (id !== null && notify) onOpen(trucks.get(id)?.id);
  }

  function stop() {
    if (frame !== null) cancelAnimationFrame(frame);
    frame = null;
  }

  function render() {
    frame = null;
    if (disposed || document.hidden) return;
    const time = Date.now() - truckPlaybackDelay;
    const frameAt = performance.now();
    let moving = false;
    for (const truck of trucks.values()) {
      const step = advanceMotion(truck, frameAt, time);
      if (truck.position) {
        const displayed = displayPosition(truck.id, truck.position);
        truck.marker.render(displayed);
        camera.track(truck.id, displayed);
      }
      if (step.moved) onPosition(truck.id, truck.position);
      moving ||= step.moving;
    }
    if (moving) frame = requestAnimationFrame(render);
  }

  function refresh() {
    stop();
    render();
  }

  function visibilityChanged() {
    for (const truck of trucks.values()) restartMotion(truck);
    refresh();
  }

  document.addEventListener('visibilitychange', visibilityChanged);
  const idleListener = map.addListener('idle', () => {
    if (disposed) return;
    refresh();
    if (!document.hidden) {
      for (const truck of trucks.values()) onPosition(truck.id, truck.position);
    }
  });

  return {
    clearViewportFocus() {
      camera.clearViewportFocus();
    },
    refreshViewport() {
      if (!disposed) camera.refreshViewport();
    },
    setInitialTruck(id: string | null | undefined) {
      if (!disposed) camera.setInitialTruck(id);
    },
    isFollowing() {
      return !disposed && camera.isFollowing();
    },
    setFollow(id: string, enabled?: boolean) {
      return disposed ? false : camera.setFollow(id, enabled);
    },
    getPosition(id: string) {
      return byTruckId(id)?.position ?? null;
    },
    clearSelection() {
      if (!disposed) {
        camera.endFollow();
        select(null);
      }
    },
    setTrucks(data: TruckReading[], points: TruckReading[]) {
      if (disposed) return;
      const incoming = new Map<string, TruckPoint[]>();
      for (const value of Array.isArray(points) ? points : []) {
        const point = createTruckPoint(value);
        if (!point || !value.truckExternalId) continue;
        if (!incoming.has(value.truckExternalId))
          incoming.set(value.truckExternalId, []);
        incoming.get(value.truckExternalId)!.push(point);
      }
      const active = new Set<string>();
      for (const value of Array.isArray(data) ? data : []) {
        const id = value.truckExternalId;
        if (!id) continue;
        const current = createTruckPoint(value);
        const next = incoming.get(id) || [];
        let truck = trucks.get(id);
        if (!truck && !current && !next.length) continue;
        active.add(id);
        if (!truck) {
          truck = {
            ...createTruckMotion(),
            id: value.truckId,
            marker: markerFactory(
              map,
              () => select(id),
              () => select(null),
            ),
          };
          trucks.set(id, truck);
        }
        truck.id = value.truckId;
        truck.marker.setVisible(isVisible(truck));
        receivePoints(truck, next, current, Date.now() - truckPlaybackDelay);
        truck.marker.update(value);
      }
      for (const [id, truck] of trucks) {
        if (active.has(id)) continue;
        if (truck.id === camera.followingId()) camera.endFollow();
        if (selectedId === id) select(null);
        truck.marker.dispose();
        trucks.delete(id);
      }
      refresh();
      camera.fitInitial(
        [...trucks.values()].map(truck => ({
          id: truck.id,
          position: truck.position,
        })),
      );
    },
    releaseCamera() {
      if (!disposed) camera.releaseCamera();
    },
    focusTruck(id: string, zoom?: number, preserveUserCamera = false) {
      if (disposed) return false;
      for (const [externalId, truck] of trucks) {
        if (truck.id !== id) continue;
        const position = getTruckPosition(
          truck.points,
          truck.playbackTime ?? Date.now() - truckPlaybackDelay,
        );
        if (!position) return false;
        select(externalId, false);
        camera.focusOn(id, position, zoom, preserveUserCamera);
        return true;
      }
      return false;
    },
    setEditingTruck(id: string | null | undefined) {
      if (disposed) return;
      editingTruckId = id ?? null;
      const following = camera.followingId();
      if (
        following !== null &&
        editingTruckId !== null &&
        following !== editingTruckId
      )
        camera.endFollow();
      for (const truck of trucks.values())
        truck.marker.setVisible(isVisible(truck));
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      selectedId = null;
      camera.dispose();
      idleListener.remove();
      stop();
      document.removeEventListener('visibilitychange', visibilityChanged);
      for (const truck of trucks.values()) truck.marker.dispose();
      trucks.clear();
    },
  };
}
