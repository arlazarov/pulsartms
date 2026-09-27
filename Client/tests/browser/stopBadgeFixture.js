import { loadGoogleMaps } from '../../Scripts/fleetMap/provider/googleMapsLoader.ts';
import { createGpuScene } from '../../Scripts/fleetMap/rendering/gpuScene.ts';
import {
  currentRouteColor,
  futureRouteColor,
} from '../../Scripts/fleetMap/rendering/routePalette.ts';

// Stop badges as the production scene draws them: each label (P, D, D1, D2)
// in each state a reader meets - on the current route, on a later load,
// highlighted, and done - on a real map, so their sizes can be compared.
const key = window.mapsKey;
delete window.mapsKey;
await loadGoogleMaps(key);
const host = document.getElementById('map');
const map = new google.maps.Map(host, {
  center: { lat: 40, lng: -80 },
  zoom: 9,
  mapId: 'DEMO_MAP_ID',
  mapTypeId: 'roadmap',
  colorScheme: window.mapScheme ?? 'LIGHT',
  clickableIcons: false,
  renderingType: google.maps.RenderingType.VECTOR,
  tilt: 0,
  heading: 0,
  disableDefaultUI: true,
});
const scene = createGpuScene(map);
const labels = [
  ['P', 'Pickup'],
  ['D', 'Delivery'],
  ['D1', 'Delivery'],
  ['D2', 'Delivery'],
];
const states = ['current', 'future', 'highlighted', 'done'];
const markers = [];
states.forEach((state, row) =>
  labels.forEach(([label, job], column) => {
    const marker = new scene.StopMarker({
      map,
      position: { lat: 40.6 - row * 0.28, lng: -80.6 + column * 0.4 },
      number: label,
      order: row * 10 + column,
      job,
      color: state === 'future' ? futureRouteColor(0) : currentRouteColor,
    });
    if (state === 'highlighted') marker.highlighted = true;
    if (state === 'done') marker.setDone(true);
    markers.push(marker);
  }),
);
await new Promise(resolve => {
  google.maps.event.addListenerOnce(map, 'idle', resolve);
  setTimeout(resolve, 4000);
});
window.badges = { ready: true, states, labels: labels.map(x => x[0]) };
