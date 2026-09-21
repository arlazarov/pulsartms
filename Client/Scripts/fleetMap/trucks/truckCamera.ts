import type { RoutePoint } from '../contracts.d.ts';

type Center = google.maps.LatLng | google.maps.LatLngLiteral;

// The part of the map's viewport a truck may stand in: the middle of what
// the inspector and the editors have left uncovered.
export type CameraViewport = {
  center(position: google.maps.LatLngLiteral, zoom?: number): Center;
  captureCenter(): (
    position: google.maps.LatLngLiteral,
    zoom?: number,
  ) => Center;
  padding(base: number): number | google.maps.Padding;
};

// A truck as the camera needs it: which truck it is and where it is drawn.
export type CameraTruck = { id: string; position: RoutePoint | null };

export type TruckCameraOptions = {
  // Where a truck is drawn now, by the truck's own id.
  truckPosition: (truckId: string) => RoutePoint | null;
  onFollowChange?: (following: boolean) => void;
  // Runs the first camera move, optionally once the map has settled; called
  // with nothing when the user has taken the camera over.
  initialCamera?: (
    change?: (initial: boolean) => void,
    waitForIdle?: boolean,
  ) => void;
  viewport?: CameraViewport;
};

/**
 * The camera over the trucks: the first move it makes, the truck it is
 * following, and the moment the user takes it over. It knows nothing about
 * telemetry - the layer hands it positions, and it decides where to stand.
 *
 * Positions cross the line the way the server says them, latitude and
 * longitude, not the map's lat and lng.
 */
export function createTruckCamera(
  map: google.maps.Map,
  {
    truckPosition,
    onFollowChange = () => {},
    initialCamera = (change, _waitForIdle) => change?.(true),
    // A stand-in for the real viewport, used when a test mounts the layer
    // alone. It takes what the real one takes and hands the position back.
    viewport = {
      center: (position, _zoom) => position,
      captureCenter: () => (position, _zoom) => position,
      padding: value => value,
    },
  }: TruckCameraOptions,
) {
  let fitted = false;
  let userCamera = false;
  let initialTruckId: string | null = null;
  let followingId: string | null = null;
  let followedPosition: RoutePoint | null = null;
  let followCenter: ReturnType<CameraViewport['captureCenter']> | null = null;
  let viewportFocusId: string | null = null;
  let updatingFollowCamera = false;

  function endFollow() {
    viewportFocusId = null;
    if (followingId === null) return;
    followingId = null;
    followedPosition = null;
    followCenter = null;
    onFollowChange(false);
  }

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

  const dragListener = map.addListener('dragstart', endFollow);
  const zoomListener = map.addListener('zoom_changed', () => {
    if (!updatingFollowCamera) viewportFocusId = null;
    if (followingId !== null && !updatingFollowCamera) endFollow();
  });

  return {
    followingId: () => followingId,
    isFollowing: () => followingId !== null,
    endFollow,
    releaseCamera() {
      keepUserCamera();
      endFollow();
    },
    setInitialTruck(id: string | null | undefined) {
      if (!fitted) initialTruckId = id ?? null;
    },
    clearViewportFocus() {
      viewportFocusId = null;
    },
    // Every drawn frame of the followed truck, which is why it leaves a
    // camera that has not moved alone.
    track(truckId: string, position: RoutePoint | null) {
      if (followingId === null || truckId !== followingId || !position) return;
      if (
        position.latitude === followedPosition?.latitude &&
        position.longitude === followedPosition?.longitude
      )
        return;
      followedPosition = position;
      map.moveCamera({
        center: followCenter!({
          lat: position.latitude,
          lng: position.longitude,
        }),
      });
    },
    setFollow(id: string, enabled?: boolean) {
      if (enabled === undefined) enabled = followingId !== id;
      if (!enabled) {
        endFollow();
        return false;
      }
      const position = truckPosition(id);
      if (!position) return false;
      followingId = id;
      viewportFocusId = id;
      fitted = true;
      followedPosition = position;
      // Details may change insets, but not the active Follow screen anchor.
      followCenter = viewport.captureCenter();
      updatingFollowCamera = true;
      try {
        // Apply both atomically so animated zoom cannot overwrite Follow.
        map.moveCamera({
          zoom: 15,
          center: followCenter(
            { lat: position.latitude, lng: position.longitude },
            15,
          ),
        });
      } finally {
        updatingFollowCamera = false;
      }
      onFollowChange(true);
      return true;
    },
    refreshViewport() {
      const id = followingId ?? viewportFocusId;
      const position = id ? truckPosition(id) : null;
      if (followingId !== null) followCenter = viewport.captureCenter();
      if (position)
        map.moveCamera({
          center: viewport.center({
            lat: position.latitude,
            lng: position.longitude,
          }),
        });
    },
    // The camera half of opening a truck: the layer has already selected it.
    focusOn(
      id: string,
      position: RoutePoint,
      zoom: number | undefined,
      preserveUserCamera: boolean,
    ) {
      fitted = true;
      if (preserveUserCamera && userCamera && id === initialTruckId) return;
      viewportFocusId = id;
      initialCamera(initial => {
        const center = viewport.center(
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
          if (Number.isFinite(zoom)) map.setZoom(zoom!);
        }
      }, false);
    },
    // The one camera move made before anyone has touched anything: the truck
    // the page was opened on, or all of them at once.
    fitInitial(trucks: CameraTruck[]) {
      if (fitted) return;
      if (!trucks.length) {
        initialCamera();
        return;
      }
      const target = initialTruckId
        ? trucks.find(truck => truck.id === initialTruckId)
        : null;
      if (target?.position) {
        fitted = true;
        viewportFocusId = target.id;
        const position = target.position;
        initialCamera(
          () =>
            map.moveCamera({
              center: viewport.center({
                lat: position.latitude,
                lng: position.longitude,
              }),
            }),
          false,
        );
        return;
      }
      const bounds = new google.maps.LatLngBounds();
      for (const truck of trucks) {
        if (truck.position)
          bounds.extend({
            lat: truck.position.latitude,
            lng: truck.position.longitude,
          });
      }
      if (!bounds.isEmpty()) {
        fitted = true;
        initialCamera(() => map.fitBounds(bounds, viewport.padding(70)));
      } else initialCamera();
    },
    dispose() {
      followingId = null;
      followedPosition = null;
      followCenter = null;
      dragListener.remove();
      zoomListener.remove();
      mapElement?.removeEventListener('pointerdown', keepUserCamera);
      mapElement?.removeEventListener('wheel', onWheel);
      mapElement?.removeEventListener('keydown', keepUserCamera);
    },
  };
}
