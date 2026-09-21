// The provider map is mounted once and retained: Google fixes a map's colour
// scheme when it is constructed, so a theme change replaces the map rather
// than recolouring it, and nothing else may hold a second one.
type Mounted = {
  host: HTMLElement;
  map: google.maps.Map;
  colorScheme: string;
};

export function createMapHost(
  createMap: (
    host: HTMLElement,
    options: google.maps.MapOptions,
  ) => google.maps.Map,
  discardMap?: (map: google.maps.Map) => void,
) {
  let cached: Mounted | null = null;
  let mounted = false;
  return (
    element: HTMLElement,
    options: google.maps.MapOptions & { colorScheme?: string },
  ) => {
    if (mounted) throw new Error('Fleet map is already mounted.');
    const colorScheme = options.colorScheme ?? 'LIGHT';
    if (cached && cached.colorScheme !== colorScheme) {
      // Google fixes the scheme at construction. Keep only one provider map.
      const center = cached.map.getCenter?.();
      const zoom = cached.map.getZoom?.();
      options = { ...options, ...(center ? { center, zoom } : {}) };
      const replaced = cached;
      cached = null;
      replaced.host.remove();
      // The provider has no way to destroy a map, so whatever still listens
      // to the old one keeps it, and everything those listeners close over,
      // alive. A failure here must not stop the new map from mounting.
      try {
        discardMap?.(replaced.map);
      } catch (error) {
        console.warn('[Fleet map] The replaced map was not released.', error);
      }
    }
    if (!cached) {
      const host = element.ownerDocument.createElement('div');
      host.className = 'fleet-map-host fleet-map-host--initializing';
      element.append(host);
      try {
        cached = { host, map: createMap(host, options), colorScheme };
      } catch (error) {
        host.remove();
        throw error;
      }
    } else {
      cached.host.className = 'fleet-map-host fleet-map-host--initializing';
      element.append(cached.host);
      const {
        mapId,
        renderingType,
        center,
        zoom,
        colorScheme,
        ...mutableOptions
      } = options;
      cached.map.setOptions(mutableOptions);
    }
    mounted = true;
    let released = false;
    const map = cached!.map;
    const host = cached!.host;
    let revealed = false,
      idleListener: google.maps.MapsEventListener | null = null,
      fallback: ReturnType<typeof setTimeout> | null = null;
    function stopWaiting() {
      idleListener?.remove();
      idleListener = null;
      if (fallback !== null) clearTimeout(fallback);
      fallback = null;
    }
    function show() {
      if (released || revealed) return;
      revealed = true;
      stopWaiting();
      host.className = 'fleet-map-host';
    }
    return {
      map,
      show,
      initialCamera(change?: (initial: boolean) => void, waitForIdle = true) {
        if (released) return;
        if (revealed) {
          change?.(false);
          return;
        }
        if (!change) {
          show();
          return;
        }
        stopWaiting();
        if (waitForIdle) {
          idleListener = map.addListener('idle', show);
          // An unchanged provider camera may not emit idle; startup must still finish.
          fallback = setTimeout(show, 2000);
        }
        try {
          change(true);
        } catch (error) {
          show();
          throw error;
        }
        if (!waitForIdle) show();
      },
      release() {
        if (released) return;
        released = true;
        stopWaiting();
        host.remove();
        mounted = false;
      },
    };
  };
}
