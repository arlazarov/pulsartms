import type {
  RouteChoicePreview,
  RouteEditorUpdate,
  RoutePoint,
} from '../contracts.d.ts';
import {
  currentRouteLineColor,
  futureRouteColor,
} from '../rendering/routePalette.ts';

// What the editor is holding: the update the page last sent, with the
// preview it refers to - an update that leaves the preview out means the
// one already on the map.
type EditorPayload = RouteEditorUpdate & { preview: RouteChoicePreview };

type EditorLine = {
  setOptions(options: Record<string, unknown>): void;
  setPath(path: google.maps.LatLngLiteral[]): void;
  setMap(map: google.maps.Map | null): void;
};

// A marker the dispatcher can take hold of. The leg it belongs to is the
// editor's own bookkeeping, hung on the marker itself.
type MapMarker = {
  position?: google.maps.LatLngLiteral | google.maps.LatLng | null;
  addListener(event: string, handler: () => void): { remove(): void };
  setMap?(map: google.maps.Map | null): void;
  map?: google.maps.Map | null;
};
type DragHandle = MapMarker & { leg: number | null };

const literal = (p: RoutePoint) => ({ lat: p.latitude, lng: p.longitude });
const value = (p: any, key: 'lat' | 'lng') =>
  typeof p[key] === 'function' ? p[key]() : p[key];

export function createRouteEditor(
  map: google.maps.Map,
  Polyline: new (options: Record<string, unknown>) => EditorLine,
  StopMarker: new (options: Record<string, unknown>) => {
    setMap?(map: google.maps.Map | null): void;
    map?: google.maps.Map | null;
  },
  Marker: (new (options: Record<string, unknown>) => MapMarker) | null,
  notify: (method: string, ...args: unknown[]) => void,
  fitPadding: (value: number) => number | google.maps.Padding,
) {
  let payload: EditorPayload | null = null,
    lines: {
      line: EditorLine;
      number: number;
      hover: (info: { coordinate?: number[] } | null) => void;
    }[] = [],
    stops: {
      setMap?: (map: google.maps.Map | null) => void;
      map?: google.maps.Map | null;
    }[] = [],
    markers: DragHandle[] = [],
    listeners: { remove(): void }[] = [],
    handle: DragHandle | null = null,
    dragging = false,
    disposed = false;
  let fitted: string | null = null;
  function clearHandles() {
    for (const marker of markers) {
      if (marker.setMap) marker.setMap(null);
      else marker.map = null;
    }
    for (const listener of listeners) listener.remove();
    markers = [];
    listeners = [];
    handle = null;
    dragging = false;
  }
  function clear() {
    clearHandles();
    for (const entry of lines) entry.line.setMap(null);
    for (const stop of stops) {
      if (stop.setMap) stop.setMap(null);
      else stop.map = null;
    }
    lines = [];
    stops = [];
  }
  function dragMarker(
    point: RoutePoint,
    id: string | null,
    leg: number | null,
    title: string,
  ) {
    const document = map.getDiv().ownerDocument;
    const content = document.createElement('span');
    content.className = 'route-editor-handle';
    content.textContent = '◇';
    const marker = new Marker!({
      map,
      position: literal(point),
      content,
      title,
      gmpDraggable: true,
      zIndex: 100,
    }) as DragHandle;
    marker.leg = leg;
    const session = payload!.session;
    listeners.push(
      marker.addListener('dragstart', () => {
        dragging = true;
      }),
    );
    listeners.push(
      marker.addListener('dragend', () => {
        dragging = false;
        if (
          !disposed &&
          payload?.session === session &&
          markers.includes(marker)
        )
          notify(
            'OnRouteViaChanged',
            session,
            id,
            marker.leg,
            value(marker.position, 'lat'),
            value(marker.position, 'lng'),
          );
      }),
    );
    markers.push(marker);
    return marker;
  }
  function hover(info: { coordinate?: number[] } | null, leg: number) {
    if (!Marker || !payload?.editing || dragging || !info?.coordinate) return;
    const [longitude, latitude] = info.coordinate!;
    if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) return;
    if (!handle)
      handle = dragMarker(
        { latitude, longitude },
        null,
        leg,
        'Drag to change road',
      );
    else handle.position = { lat: latitude, lng: longitude };
    handle.leg = leg;
  }
  return {
    get active() {
      return payload !== null;
    },
    get truckId() {
      return payload?.preview.truckId ?? null;
    },
    set(next: RouteEditorUpdate | null) {
      if (disposed) return;
      if (!next) {
        clear();
        payload = null;
        fitted = null;
        return;
      }
      if (
        !next.preview &&
        (next.session !== payload?.session ||
          next.previewId !== payload?.preview.id)
      )
        return;
      const preview = next.preview ?? payload!.preview;
      const { selected, editing, session } = next;
      const sameGeometry =
        payload?.session === session && payload.preview.id === preview.id;
      const handlesChanged =
        !sameGeometry ||
        payload!.selected !== selected ||
        payload!.editing !== editing;
      if (!sameGeometry) clear();
      payload = { ...next, preview };
      if (!sameGeometry) {
        for (const option of preview.options) {
          for (const [index, leg] of option.route.legs.entries()) {
            const line = new Polyline({
              map,
              routeRole: 'preview',
              onClick: () => {
                if (
                  lines.some(entry => entry.line === line) &&
                  !payload?.addPoint
                )
                  notify('OnRouteOptionSelected', session, option.number);
                return true;
              },
            });
            line.setPath(leg.points.map(literal));
            lines.push({
              line,
              number: option.number,
              hover: (info: { coordinate?: number[] } | null) => {
                if (lines.some(entry => entry.line === line))
                  hover(info, index);
              },
            });
          }
        }
        for (const [index, stop] of preview.stops.entries())
          stops.push(
            new StopMarker({
              map,
              position: literal(stop.point),
              number: String(index + 1),
              job: stop.job,
              routeRole: 'preview',
            }),
          );
      }
      for (const entry of lines) {
        const active = entry.number === selected;
        entry.line.setOptions({
          routeColor: active
            ? currentRouteLineColor
            : futureRouteColor(entry.number - 1),
          strokeWeight: active ? 5 : 3,
          zIndex: active ? 20 : 15,
          onHover: active && editing ? entry.hover : undefined,
        });
      }
      if (handlesChanged) {
        clearHandles();
        if (editing && Marker) {
          for (const via of preview.viaPoints)
            dragMarker(
              via.point,
              via.id,
              preview.stops.findIndex(stop => stop.id === via.beforeStopId) - 1,
              `Drag ${via.label}`,
            );
          const first = preview.options.find(
            option => option.number === selected,
          )?.route.legs[0];
          if (first && first.points.length > 1) {
            handle = dragMarker(
              first.points[Math.floor(first.points.length / 2)],
              null,
              0,
              'Drag to change road',
            );
            handle.leg = 0;
          }
        }
      }
      if (fitted !== session) {
        const bounds = new google.maps.LatLngBounds();
        for (const option of preview.options)
          for (const leg of option.route.legs)
            for (const point of leg.points) bounds.extend(literal(point));
        map.fitBounds(bounds, fitPadding(40));
        fitted = session;
      }
    },
    click(event: { latLng?: unknown } | undefined) {
      if (!payload) return false;
      if (payload.editing && payload.addPoint && event?.latLng)
        notify(
          'OnRouteViaChanged',
          payload.session,
          null,
          -1,
          value(event.latLng, 'lat'),
          value(event.latLng, 'lng'),
        );
      return true;
    },
    dispose() {
      disposed = true;
      clear();
      payload = null;
    },
  };
}
