import { createTruckPoint, mergeTruckPoints } from './truckPoints.js';
import {
  advancePlaybackTime,
  blendTruckPosition,
  getTruckPosition,
  truckPlaybackDelay,
  truckPlaybackResumeGap,
  truckTransitionDuration,
} from './truckPlayback.js';

/**
 * Every truck on the map: where it is, where it is going, and which one the
 * page has open.
 *
 * @param {(truckId: string) => void} onOpen
 * @param {(truckId: string, position: import('../contracts.d.ts').RoutePoint) => void} onPosition
 * @param {(following: boolean) => void} onFollowChange
 *   Whether the camera is now following the open truck.
 * @param {(change?: (initial: boolean) => void, waitForIdle?: boolean) => void} initialCamera
 *   Runs the first camera move, optionally once the map has settled; called
 *   with nothing when the user has taken the camera over.
 * @param {(truckId: string, position: import('../contracts.d.ts').RoutePoint) =>
 *   import('../contracts.d.ts').RoutePoint} displayPosition
 *   Where a truck should be drawn now - the playback may have it between
 *   two reported points. Positions cross the line the way the server says
 *   them, latitude and longitude, not the map's lat and lng.
 */
export function createTruckLayer(
  map,
  onOpen = () => {},
  onPosition = () => {},
  displayPosition = (_, position) => position,
  markerFactory,
  onFollowChange = () => {},
  initialCamera = (change, _waitForIdle) => change?.(true),
  // A stand-in for the real viewport, used when a test mounts this layer
  // alone. It takes what the real one takes and hands the position back.
  cameraViewport = {
    center: (position, _zoom) => position,
    captureCenter: () => (position, _zoom) => position,
    padding: value => value,
  },
) {
  const trucks = new Map();
  let selectedId = null;
  let editingTruckId = null;
  const isVisible = truck =>
    editingTruckId === null || truck.id === editingTruckId;
  let frame = null;
  let disposed = false;
  let fitted = false;
  let initialTruckId = null;
  let userCamera = false;
  let followingId = null,
    followedPosition = null;
  let followCenter = null;
  let viewportFocusId = null;
  let updatingFollowCamera = false;
  // Late first telemetry must not move a camera the user is already using.
  const mapElement = map.getDiv?.();
  const keepUserCamera = () => {
    fitted = true;
    userCamera = true;
    viewportFocusId = null;
    initialCamera();
  };
  const onWheel = () => {
    keepUserCamera();
    endFollow();
  };
  mapElement?.addEventListener('pointerdown', keepUserCamera, {
    passive: true,
  });
  mapElement?.addEventListener('wheel', onWheel, { passive: true });
  mapElement?.addEventListener('keydown', keepUserCamera);

  function endFollow() {
    viewportFocusId = null;
    if (followingId === null) return;
    followingId = null;
    followedPosition = null;
    followCenter = null;
    onFollowChange(false);
  }

  function followPosition(truck, position) {
    if (followingId === null || truck.id !== followingId || !position) return;
    if (
      position.latitude === followedPosition?.latitude &&
      position.longitude === followedPosition?.longitude
    )
      return;
    followedPosition = position;
    map.moveCamera({
      center: followCenter({
        lat: position.latitude,
        lng: position.longitude,
      }),
    });
  }

  function select(id, notify = true) {
    if (disposed) return;
    if (id === selectedId) {
      if (id !== null && notify) onOpen(trucks.get(id)?.id);
      return;
    }
    trucks.get(selectedId)?.marker.setSelected(false);
    endFollow();
    selectedId = id;
    trucks.get(id)?.marker.setSelected(true);
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
      const elapsed =
        truck.playbackFrameAt === undefined
          ? 0
          : frameAt - truck.playbackFrameAt;
      truck.playbackFrameAt = frameAt;
      // A suspended tab or pruned history resumes at the buffered live position,
      // never as a six-second animation across the missing journey.
      if (
        truck.playbackTime === undefined ||
        elapsed > truckPlaybackResumeGap ||
        truck.playbackTime < truck.points[0]?.gpsTime
      ) {
        truck.playbackTime = undefined;
        truck.transition = null;
      }
      truck.playbackTime = advancePlaybackTime(
        truck.playbackTime,
        truck.points.at(-1)?.gpsTime,
        time,
        elapsed,
      );
      const target = getTruckPosition(truck.points, truck.playbackTime);
      const progress = truck.transition
        ? (performance.now() - truck.transition.start) / truckTransitionDuration
        : 1;
      const previous = truck.position;
      truck.position = blendTruckPosition(
        truck.transition?.from,
        target,
        progress,
      );
      if (truck.position) {
        const displayed = displayPosition(truck.id, truck.position);
        truck.marker.render(displayed);
        followPosition(truck, displayed);
      }
      if (
        previous?.latitude !== truck.position?.latitude ||
        previous?.longitude !== truck.position?.longitude ||
        previous?.heading !== truck.position?.heading ||
        previous?.speed !== truck.position?.speed
      ) {
        onPosition(truck.id, truck.position);
      }
      if (progress >= 1) truck.transition = null;
      moving ||= progress < 1;
      // The target advances with wall time; buffered points still need frames.
      moving ||=
        truck.points.length > 1 &&
        truck.playbackTime < truck.points.at(-1).gpsTime;
    }
    if (moving) frame = requestAnimationFrame(render);
  }

  function refresh() {
    stop();
    render();
  }

  function visibilityChanged() {
    for (const truck of trucks.values()) {
      truck.playbackTime = undefined;
      truck.playbackFrameAt = undefined;
      truck.transition = null;
    }
    refresh();
  }

  document.addEventListener('visibilitychange', visibilityChanged);
  const dragListener = map.addListener('dragstart', endFollow);
  const zoomListener = map.addListener('zoom_changed', () => {
    if (!updatingFollowCamera) viewportFocusId = null;
    if (followingId !== null && !updatingFollowCamera) endFollow();
  });
  const idleListener = map.addListener('idle', () => {
    if (disposed) return;
    refresh();
    if (!document.hidden) {
      for (const truck of trucks.values()) onPosition(truck.id, truck.position);
    }
  });

  return {
    clearViewportFocus() {
      viewportFocusId = null;
    },
    refreshViewport() {
      if (disposed) return;
      const id = followingId ?? viewportFocusId;
      const truck = id && [...trucks.values()].find(value => value.id === id);
      const position =
        truck?.position && displayPosition(truck.id, truck.position);
      if (followingId !== null) followCenter = cameraViewport.captureCenter();
      if (position)
        map.moveCamera({
          center: cameraViewport.center({
            lat: position.latitude,
            lng: position.longitude,
          }),
        });
    },
    setInitialTruck(id) {
      if (!disposed && !fitted) initialTruckId = id ?? null;
    },
    isFollowing() {
      return !disposed && followingId !== null;
    },
    setFollow(id, enabled) {
      if (disposed) return false;
      if (enabled === undefined) enabled = followingId !== id;
      if (!enabled) {
        endFollow();
        return false;
      }
      const truck = [...trucks.values()].find(t => t.id === id);
      if (!truck?.position) return false;
      followingId = id;
      viewportFocusId = id;
      fitted = true;
      followedPosition = displayPosition(truck.id, truck.position);
      // Details may change insets, but not the active Follow screen anchor.
      followCenter = cameraViewport.captureCenter();
      updatingFollowCamera = true;
      try {
        // Apply both atomically so animated zoom cannot overwrite Follow.
        map.moveCamera({
          zoom: 15,
          center: followCenter(
            { lat: followedPosition.latitude, lng: followedPosition.longitude },
            15,
          ),
        });
      } finally {
        updatingFollowCamera = false;
      }
      onFollowChange(true);
      return true;
    },
    getPosition(id) {
      return [...trucks.values()].find(t => t.id === id)?.position ?? null;
    },
    clearSelection() {
      if (!disposed) {
        endFollow();
        select(null);
      }
    },
    setTrucks(data, points) {
      if (disposed) return;
      const incoming = new Map();
      for (const value of Array.isArray(points) ? points : []) {
        const point = createTruckPoint(value);
        if (!point || !value.truckExternalId) continue;
        if (!incoming.has(value.truckExternalId))
          incoming.set(value.truckExternalId, []);
        incoming.get(value.truckExternalId).push(point);
      }
      const active = new Set();
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
            points: [],
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
        const time = Date.now() - truckPlaybackDelay;
        const playbackTime = truck.playbackTime ?? time;
        const before = getTruckPosition(truck.points, playbackTime);
        truck.points = mergeTruckPoints(truck.points, next, current, time);
        const after = getTruckPosition(truck.points, playbackTime);
        if (
          truck.position &&
          before &&
          after &&
          (Math.abs(before.latitude - after.latitude) > 1e-9 ||
            Math.abs(before.longitude - after.longitude) > 1e-9)
        ) {
          truck.transition = { from: truck.position, start: performance.now() };
        }
        truck.marker.update(value);
      }
      for (const [id, truck] of trucks) {
        if (active.has(id)) continue;
        if (truck.id === followingId) endFollow();
        if (selectedId === id) select(null);
        truck.marker.dispose();
        trucks.delete(id);
      }
      refresh();
      if (!fitted) {
        if (!trucks.size) {
          initialCamera();
          return;
        }
        const target = initialTruckId
          ? [...trucks.values()].find(truck => truck.id === initialTruckId)
          : null;
        if (target?.position) {
          fitted = true;
          viewportFocusId = target.id;
          initialCamera(
            () =>
              map.moveCamera({
                center: cameraViewport.center({
                  lat: target.position.latitude,
                  lng: target.position.longitude,
                }),
              }),
            false,
          );
          return;
        }
        const bounds = new google.maps.LatLngBounds();
        for (const truck of trucks.values()) {
          if (truck.position)
            bounds.extend({
              lat: truck.position.latitude,
              lng: truck.position.longitude,
            });
        }
        if (!bounds.isEmpty()) {
          fitted = true;
          initialCamera(() =>
            map.fitBounds(bounds, cameraViewport.padding(70)),
          );
        } else initialCamera();
      }
    },
    releaseCamera() {
      if (disposed) return;
      keepUserCamera();
      endFollow();
    },
    focusTruck(id, zoom, preserveUserCamera = false) {
      if (disposed) return false;
      for (const [externalId, truck] of trucks) {
        if (truck.id !== id) continue;
        const position = getTruckPosition(
          truck.points,
          truck.playbackTime ?? Date.now() - truckPlaybackDelay,
        );
        if (!position) return false;
        fitted = true;
        select(externalId, false);
        if (preserveUserCamera && userCamera && id === initialTruckId)
          return true;
        viewportFocusId = id;
        initialCamera(initial => {
          const center = cameraViewport.center(
            { lat: position.latitude, lng: position.longitude },
            zoom ?? map.getZoom?.(),
          );
          if (initial)
            map.moveCamera({
              center,
              ...(Number.isFinite(zoom) ? { zoom } : {}),
            });
          else {
            map.panTo(center);
            if (Number.isFinite(zoom)) map.setZoom(zoom);
          }
        }, false);
        return true;
      }
      return false;
    },
    setEditingTruck(id) {
      if (disposed) return;
      editingTruckId = id ?? null;
      if (
        followingId !== null &&
        editingTruckId !== null &&
        followingId !== editingTruckId
      )
        endFollow();
      for (const truck of trucks.values())
        truck.marker.setVisible(isVisible(truck));
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      selectedId = null;
      followingId = null;
      followedPosition = null;
      followCenter = null;
      dragListener.remove();
      zoomListener.remove();
      idleListener.remove();
      mapElement?.removeEventListener('pointerdown', keepUserCamera);
      mapElement?.removeEventListener('wheel', onWheel);
      mapElement?.removeEventListener('keydown', keepUserCamera);
      stop();
      document.removeEventListener('visibilitychange', visibilityChanged);
      for (const truck of trucks.values()) truck.marker.dispose();
      trucks.clear();
    },
  };
}
