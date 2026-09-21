import { loadGoogleMaps } from '../fleetMap/provider/googleMapsLoader.ts';

// A stop as the dispatch pages hand it over: which stop, what its pin says
// and where it is.
export type StopMapPoint = {
  id: string;
  number: string;
  name?: string;
  latitude: number;
  longitude: number;
};

// What a leg of the road means, which decides the colour it is drawn in.
type RoadMeaning = { cargoState?: string };

// One stop map: the map itself, what is drawn on it, and what was asked for
// while it was still loading.
type StopMap = {
  // The latest call. An earlier one that finishes afterwards sees that it
  // is no longer the latest and does nothing.
  request: object;
  markers: google.maps.marker.AdvancedMarkerElement[];
  lines: google.maps.Polyline[];
  roads: string | null;
  geometry: string | null;
  map: google.maps.Map | null;
  selected?: string | null;
  points?: StopMapPoint[];
  paths?: StopMapPoint[][];
  satellite?: boolean;
  activatedStop?: string | null;
  pendingView?: { selected: string; satellite: boolean } | null;
};

export function isMobile() {
  return window.matchMedia('(max-width: 799px)').matches;
}

export function revealStop(list: HTMLElement | null, stopId: string) {
  if (!list?.isConnected) return;
  const row = [...list.children].find(
    item => (item as HTMLElement).dataset.stopId === stopId,
  );
  if (!row) return;
  const viewport = list.getBoundingClientRect();
  const bounds = row.getBoundingClientRect();
  if (bounds.top < viewport.top || bounds.height > list.clientHeight) {
    list.scrollTop += bounds.top - viewport.top;
  } else if (bounds.bottom > viewport.top + list.clientHeight) {
    list.scrollTop += bounds.bottom - viewport.top - list.clientHeight;
  }
}

const stopMaps = new WeakMap<HTMLElement, StopMap>();

export async function showStopMap(
  element: HTMLElement,
  key: string,
  points: StopMapPoint[],
  selected: string | null,
  paths: StopMapPoint[][] = [],
  meanings: RoadMeaning[] = [],
) {
  const request = {};
  let state = stopMaps.get(element);
  if (!state) {
    state = {
      request,
      markers: [],
      lines: [],
      roads: null,
      geometry: null,
      map: null,
    };
    stopMaps.set(element, state);
  }
  state.request = request;
  state.selected = selected;
  await loadGoogleMaps(key);
  if (
    !element.isConnected ||
    stopMaps.get(element) !== state ||
    state.request !== request
  )
    return;
  state.map ??= new google.maps.Map(element, {
    mapId: 'DEMO_MAP_ID',
    center: { lat: 39, lng: -96 },
    zoom: 4,
    colorScheme:
      document.documentElement.dataset.theme === 'dark' ? 'DARK' : 'LIGHT',
    mapTypeControl: false,
    streetViewControl: false,
    fullscreenControl: false,
    gestureHandling: 'greedy',
    cameraControl: false,
    zoomControl: false,
  });
  state.points = points;
  state.paths = paths;
  const geometry = JSON.stringify(points);
  if (geometry !== state.geometry) {
    state.markers.forEach(marker => {
      marker.map = null;
    });
    const locations = new Map<string, StopMapPoint[]>();
    points.forEach(point => {
      const location = `${point.latitude.toFixed(6)},${point.longitude.toFixed(6)}`;
      if (!locations.has(location)) locations.set(location, []);
      locations.get(location)!.push(point);
    });
    state.markers = [...locations.values()].map(group => {
      const labels = group.map(point => {
        const label = document.createElement('span');
        label.className = 'dispatch-stop-map__pin';
        label.textContent = point.number;
        label.dataset.stopId = point.id;
        return label;
      });
      let content: HTMLElement = labels[0];
      if (labels.length > 1) {
        content = document.createElement('span');
        content.className = 'dispatch-stop-map__pin-group';
        content.append(...labels);
      }
      return new google.maps.marker.AdvancedMarkerElement({
        map: state.map,
        position: { lat: group[0].latitude, lng: group[0].longitude },
        title: group.map(point => point.number + ' · ' + point.name).join('; '),
        content,
      });
    });
    state.geometry = geometry;
    if (points.length) {
      const bounds = new google.maps.LatLngBounds();
      points.forEach(point =>
        bounds.extend({ lat: point.latitude, lng: point.longitude }),
      );
      state.map.fitBounds(bounds, 32);
      if (locations.size === 1) state.map.setZoom(12);
    }
  }
  selectStopMap(element, state.selected);
  const roads = JSON.stringify([paths, meanings]);
  if (roads !== state.roads) {
    state.lines.forEach(line => line.setMap(null));
    const color = getComputedStyle(element)
      .getPropertyValue('--dispatch-road-color')
      .trim();
    const emptyColor = getComputedStyle(element)
      .getPropertyValue('--dispatch-empty-road-color')
      .trim();
    state.lines = paths
      .map((points, index) => ({ points, meaning: meanings[index] }))
      .filter(path => path.points.length >= 2)
      .map(
        path =>
          new google.maps.Polyline({
            map: state.map,
            path: path.points.map(point => ({
              lat: point.latitude,
              lng: point.longitude,
            })),
            strokeColor: ['Empty', 'Bobtail'].includes(
              path.meaning?.cargoState ?? '',
            )
              ? emptyColor
              : color,
            strokeOpacity: 0.9,
            strokeWeight: 4,
            clickable: false,
          }),
      );
    state.roads = roads;
  }
  if (state.pendingView) applyStopMapView(state);
}

export function selectStopMap(element: HTMLElement, selected: string | null) {
  const state = stopMaps.get(element);
  if (!state) return;
  state.selected = selected;
  state.markers.forEach(marker => {
    const content = marker.content as HTMLElement;
    const labels: Iterable<HTMLElement> = content.dataset.stopId
      ? [content]
      : content.querySelectorAll<HTMLElement>('[data-stop-id]');
    let active = false;
    for (const label of labels) {
      const matches = label.dataset.stopId === selected;
      label.classList.toggle('is-selected', matches);
      active ||= matches;
    }
    marker.zIndex = active ? 1 : 0;
  });
}

export function activateStopMap(element: HTMLElement, selected: string) {
  const state = stopMaps.get(element);
  if (!state) return;
  state.satellite = state.activatedStop === selected ? !state.satellite : false;
  state.activatedStop = selected;
  state.pendingView = { selected, satellite: state.satellite };
  applyStopMapView(state);
}

function applyStopMapView(state: StopMap) {
  if (!state.map || !state.points || !state.pendingView) return;
  const { selected, satellite } = state.pendingView;
  state.pendingView = null;
  if (satellite) {
    const point = state.points.find(point => point.id === selected);
    if (!point) return;
    state.map!.setMapTypeId('satellite');
    state.map!.setCenter({ lat: point.latitude, lng: point.longitude });
    state.map!.setZoom(18);
    state.map!.setTilt(0);
    return;
  }
  state.map!.setMapTypeId('roadmap');
  const points = [...state.points, ...(state.paths ?? []).flat()];
  if (!points.length) return;
  const bounds = new google.maps.LatLngBounds();
  points.forEach(point =>
    bounds.extend({
      lat: point.latitude,
      lng: point.longitude,
    }),
  );
  state.map!.fitBounds(bounds, 32);
  if (
    points.every(
      point =>
        point.latitude === points[0].latitude &&
        point.longitude === points[0].longitude,
    )
  )
    state.map!.setZoom(12);
}

export function disposeStopMap(element: HTMLElement) {
  const state = stopMaps.get(element);
  if (!state) return;
  stopMaps.delete(element);
  state.markers.forEach(marker => {
    marker.map = null;
  });
  state.lines.forEach(line => line.setMap(null));
  if (state.map) google.maps.event.clearInstanceListeners(state.map);
}
