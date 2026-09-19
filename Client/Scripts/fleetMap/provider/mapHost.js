export function createMapHost(createMap) {
  let cached;
  let mounted = false;
  return (element, options) => {
    if (mounted) throw new Error('Fleet map is already mounted.');
    const colorScheme = options.colorScheme ?? 'LIGHT';
    if (cached && cached.colorScheme !== colorScheme) {
      // Google fixes the scheme at construction. Keep only one provider map.
      const center = cached.map.getCenter?.();
      const zoom = cached.map.getZoom?.();
      options = { ...options, ...(center ? { center, zoom } : {}) };
      cached.host.remove();
      cached = null;
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
    let revealed = false,
      idleListener = null,
      fallback = null;
    function stopWaiting() {
      idleListener?.remove();
      idleListener = null;
      clearTimeout(fallback);
      fallback = null;
    }
    function show() {
      if (released || revealed) return;
      revealed = true;
      stopWaiting();
      cached.host.className = 'fleet-map-host';
    }
    return {
      map: cached.map,
      show,
      initialCamera(change, waitForIdle = true) {
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
          idleListener = cached.map.addListener('idle', show);
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
        cached.host.remove();
        mounted = false;
      },
    };
  };
}
