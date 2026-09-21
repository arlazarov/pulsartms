// The vector map paints on its own schedule; this asks it for one frame and
// tells the caller when that frame has been drawn.
export function createMapRepaint(
  map: google.maps.Map,
  createOverlay: () => google.maps.WebGLOverlayView = () =>
    new google.maps.WebGLOverlayView(),
) {
  let overlay: google.maps.WebGLOverlayView | null = null;
  let disposed = false;
  return {
    request(ready: () => void) {
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
