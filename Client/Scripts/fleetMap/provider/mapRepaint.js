export function createMapRepaint(
  map,
  createOverlay = () => new google.maps.WebGLOverlayView(),
) {
  let overlay = null;
  let disposed = false;
  return {
    request(ready) {
      if (disposed || overlay) return;
      if (map.getRenderingType?.() !== 'VECTOR') {
        ready();
        return;
      }
      const current = createOverlay();
      overlay = current;
      current.onAdd = () => {};
      current.onRemove = () => {};
      current.onContextLost = () => {};
      current.onContextRestored = () => {
        if (!disposed && overlay === current) current.requestRedraw();
      };
      current.onDraw = () => {
        // Reveal only after other overlays have consumed this camera frame.
        queueMicrotask(() => {
          if (disposed || overlay !== current) return;
          overlay = null;
          current.setMap(null);
          ready();
        });
      };
      current.setMap(map);
    },
    dispose() {
      disposed = true;
      overlay?.setMap(null);
      overlay = null;
    },
  };
}
