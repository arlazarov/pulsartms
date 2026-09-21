// A rectangle of the map's own element, in its pixels.
type Area = { x: number; y: number; width: number; height: number };

const selectors = [
  '.fleet-map-info-reserved',
  '.fuel-plan-editor',
  '.route-editor',
];

export function createCameraViewport(
  element: HTMLElement,
  map: google.maps.Map,
) {
  const view = element.ownerDocument?.defaultView;
  const stage = element.parentElement;
  let bounds: DOMRect | null = null,
    region: Area | null = null,
    disposed = false;
  let signature = '',
    changed: () => void = () => {};
  const observed = new Set<HTMLElement>();
  const Resize = view?.ResizeObserver;
  const Mutation = view?.MutationObserver;
  const resize = typeof Resize === 'function' ? new Resize(refresh) : null;
  const mutation =
    typeof Mutation === 'function' ? new Mutation(refresh) : null;

  function refresh() {
    if (disposed) return;
    measure();
    // Overlay disclosure updates future focus insets, never the current camera.
    const next = [bounds?.width, bounds?.height].join(':');
    if (next !== signature) {
      signature = next;
      changed();
    }
  }

  function measure() {
    const inspector = stage?.querySelector?.(selectors[0]);
    const overlays = selectors
      .map(selector => stage?.querySelector?.(selector))
      .filter((overlay): overlay is HTMLElement => Boolean(overlay));
    const nodes: HTMLElement[] = [element, ...overlays];
    for (const node of observed)
      if (!nodes.includes(node)) {
        resize?.unobserve(node);
        observed.delete(node);
      }
    for (const node of nodes)
      if (!observed.has(node)) {
        resize?.observe(node);
        observed.add(node);
      }
    const next = element.getBoundingClientRect?.();
    bounds = next?.width > 0 && next?.height > 0 ? next : null;
    region = null;
    if (!bounds) return;
    let areas: Area[] = [
      { x: 0, y: 0, width: bounds.width, height: bounds.height },
    ];
    let covered = false;
    for (const overlay of overlays) {
      let box = overlay.getBoundingClientRect?.();
      // The panel is told how far it stands from the edge, so its own rule
      // can keep that gap; measuring it again is what that may have changed.
      if (
        (overlay === inspector || overlay.matches?.('.route-editor')) &&
        box?.width > 0 &&
        overlay.style
      ) {
        const inset = `${Math.max(0, Math.min(box.left - bounds.left, bounds.right - box.right))}px`;
        if (
          overlay.style.getPropertyValue('--map-inspector-side-gap') !== inset
        ) {
          overlay.style.setProperty('--map-inspector-side-gap', inset);
          box = overlay.getBoundingClientRect();
        }
      }
      if (
        !box ||
        box.width === 0 ||
        box.height === 0 ||
        box.right <= bounds.left ||
        box.left >= bounds.right ||
        box.bottom <= bounds.top ||
        box.top >= bounds.bottom
      )
        continue;
      covered = true;
      const cut = {
        left: Math.max(0, box.left - bounds.left),
        right: Math.min(bounds.width, box.right - bounds.left),
        top: Math.max(0, box.top - bounds.top),
        bottom: Math.min(bounds.height, box.bottom - bounds.top),
      };
      areas = areas.flatMap(area => {
        const right = area.x + area.width,
          bottom = area.y + area.height;
        if (
          cut.right <= area.x ||
          cut.left >= right ||
          cut.bottom <= area.y ||
          cut.top >= bottom
        )
          return [area];
        return [
          { ...area, width: Math.max(0, cut.left - area.x) },
          {
            ...area,
            x: Math.max(area.x, cut.right),
            width: Math.max(0, right - cut.right),
          },
          { ...area, height: Math.max(0, cut.top - area.y) },
          {
            ...area,
            y: Math.max(area.y, cut.bottom),
            height: Math.max(0, bottom - cut.bottom),
          },
        ].filter(candidate => candidate.width > 0 && candidate.height > 0);
      });
    }
    if (covered && areas.length)
      region = areas.reduce((best, area) =>
        area.width * area.height > best.width * best.height ? area : best,
      );
  }

  function centerOffset() {
    if (!bounds || !region) return null;
    return {
      x: bounds.width / 2 - region.x - region.width / 2,
      y: bounds.height / 2 - region.y - region.height / 2,
    };
  }

  function center(
    position: google.maps.LatLngLiteral,
    zoom: number | undefined = map.getZoom?.(),
    offset: { x: number; y: number } | null = centerOffset(),
  ): google.maps.LatLng | google.maps.LatLngLiteral {
    const projection = map.getProjection?.();
    if (disposed || !offset || !projection || !Number.isFinite(zoom))
      return position;
    const point = projection.fromLatLngToPoint(
      new google.maps.LatLng(position),
    );
    if (!point) return position;
    const scale = 2 ** zoom!;
    return (
      projection.fromPointToLatLng(
        new google.maps.Point(
          point.x + offset.x / scale,
          point.y + offset.y / scale,
        ),
      ) ?? position
    );
  }

  // Only stage children are observed; provider DOM churn never triggers layout reads.
  if (stage) mutation?.observe(stage, { childList: true });
  view?.addEventListener?.('resize', refresh);
  refresh();
  return {
    refresh,
    onChange(callback: () => void) {
      changed = callback;
    },
    center,
    captureCenter() {
      const offset = centerOffset();
      return (position: google.maps.LatLngLiteral, zoom?: number) =>
        center(position, zoom, offset);
    },
    padding(base: number) {
      if (disposed || !bounds || !region) return base;
      const gap = Math.min(base, region.width / 4, region.height / 4);
      return {
        left: region.x + gap,
        right: bounds.width - region.x - region.width + gap,
        top: region.y + gap,
        bottom: bounds.height - region.y - region.height + gap,
      };
    },
    dispose() {
      disposed = true;
      changed = () => {};
      resize?.disconnect();
      mutation?.disconnect();
      view?.removeEventListener?.('resize', refresh);
      observed.clear();
      bounds = null;
      region = null;
    },
  };
}
