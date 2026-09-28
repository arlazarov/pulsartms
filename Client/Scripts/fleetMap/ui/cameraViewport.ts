// A rectangle of the map's own element, in its pixels.
type Area = { x: number; y: number; width: number; height: number };

// How far inside the free region a revealed point is placed, and how long a
// pick is kept in view while its card opens and settles.
const revealMargin = 40;
const revealPatience = 8000;

const selectors = [
  '.fleet-map-info-reserved',
  '.fuel-plan-editor',
  '.route-editor',
  // On the wide workspace the map lies under the list and the chain too.
  '.fleet-truck-list',
  '.fleet-trip-chain',
  // The map's own tool bar covers it too.
  '.fleet-map-controls',
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
  // What was just picked: where it is, where that stood on the element when
  // the camera was last at rest, how far the map has been panned for it
  // since, and until when it is kept in view while its card settles.
  let pending: {
    position: google.maps.LatLngLiteral;
    at: { x: number; y: number };
    panned: { x: number; y: number };
    until: number;
  } | null = null;
  // What the card is about, kept after the reveal is over: a card that
  // changes shape later - opened, Details, a taller stop - reveals it
  // again, even after the reader has dragged the map away (the owner,
  // September 26).
  let current: google.maps.LatLngLiteral | null = null,
    covering = '';
  // A reader who drags the map has taken it back: nothing more is moved for
  // them. Any other move - a route fitted, a zoom, our own pan - ends in an
  // idle, where the pick's place is measured again against the camera at
  // rest, so the reveal converges whatever else moved the map meanwhile.
  // The reader's own gesture - a drag, the wheel, a pinch or a touch, a
  // key - takes the camera back for good: the pick is no longer kept in
  // view, not at the next idle and not when its card changes shape later
  // (the owner, September 28: a zoom sometimes recentred the map on the
  // last pick). Only an explicit move (a stop, a fit, Follow) reveals
  // again.
  const release = () => {
    pending = null;
    current = null;
  };
  const taken = [
    map.addListener?.('dragstart', release),
    map.addListener?.('idle', () => remeasure()),
  ];
  const gestures = ['wheel', 'touchstart', 'keydown'] as const;
  for (const gesture of gestures)
    element.addEventListener?.(gesture, release, { passive: true });
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
    // A card of another shape than last time is a new card over the pick.
    const cover = region
      ? [region.x, region.y, region.width, region.height].join(':')
      : '';
    if (cover !== covering) {
      covering = cover;
      if (cover && !pending && current) begin(current);
    }
    attemptReveal();
  }

  function begin(position: google.maps.LatLngLiteral) {
    const at = pixel(position);
    pending = at
      ? {
          position,
          at,
          panned: { x: 0, y: 0 },
          until: Date.now() + revealPatience,
        }
      : null;
  }

  // Where a position falls on the map's element, in its pixels.
  function pixel(position: google.maps.LatLngLiteral) {
    const projection = map.getProjection?.(),
      zoom = map.getZoom?.(),
      centre = map.getCenter?.();
    if (!bounds || !projection || !Number.isFinite(zoom) || !centre)
      return null;
    const point = projection.fromLatLngToPoint(
        new google.maps.LatLng(position),
      ),
      origin = projection.fromLatLngToPoint(centre);
    if (!point || !origin) return null;
    const scale = 2 ** zoom!;
    return {
      x: bounds.width / 2 + (point.x - origin.x) * scale,
      y: bounds.height / 2 + (point.y - origin.y) * scale,
    };
  }

  // The card that opens for a picked truck, stop or station used to open
  // over it. While the card settles - it opens, then grows as its details
  // arrive - each layout pass moves the pick the least distance that
  // brings it into the free region, and no distance when it is already
  // there; a pick with no card moves nothing (the owner, September 26).
  // The pick's place is the one measured when it was picked, less what has
  // been panned for it since: the camera is read once, not mid-animation.
  function remeasure() {
    if (!pending || disposed) return;
    measure();
    const at = pixel(pending.position);
    if (!at) {
      pending = null;
      return;
    }
    pending.at = at;
    pending.panned = { x: 0, y: 0 };
    attemptReveal();
  }

  function attemptReveal() {
    if (!pending || disposed) return;
    if (Date.now() > pending.until) {
      pending = null;
      return;
    }
    if (!region || typeof map.panBy !== 'function') return;
    const x = pending.at.x - pending.panned.x,
      y = pending.at.y - pending.panned.y;
    const gap = Math.min(revealMargin, region.width / 4, region.height / 4);
    const left = region.x + gap,
      right = region.x + region.width - gap,
      top = region.y + gap,
      bottom = region.y + region.height - gap;
    const dx = Math.round(x < left ? x - left : x > right ? x - right : 0),
      dy = Math.round(y < top ? y - top : y > bottom ? y - bottom : 0);
    if (dx === 0 && dy === 0) return;
    pending.panned = { x: pending.panned.x + dx, y: pending.panned.y + dy };
    map.panBy(dx, dy);
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
    reveal(position: google.maps.LatLngLiteral | null | undefined) {
      if (disposed || !position) return;
      current = position;
      // The same pick again, while it is still being brought into view,
      // is the same pick: measuring it afresh mid-pan would send it off.
      if (
        pending &&
        pending.position.lat === position.lat &&
        pending.position.lng === position.lng
      ) {
        pending.until = Date.now() + revealPatience;
        refresh();
        return;
      }
      measure();
      begin(position);
      refresh();
    },
    // Nothing is picked any more: a card that opens later is about
    // something else, and says so itself.
    forget() {
      current = null;
      pending = null;
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
      pending = null;
      current = null;
      for (const listener of taken) listener?.remove?.();
      for (const gesture of gestures)
        element.removeEventListener?.(gesture, release);
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
