// CPU-only ablation benchmark: real deck.gl layer constructors, mocked overlay.
// This does NOT measure GPU time, frame rate, Google Maps or network performance.
import {readFile} from 'node:fs/promises';
import {performance} from 'node:perf_hooks';
import {createRequire} from 'node:module';
import {createScene} from '../Client/Scripts/fleetMap/rendering/scene.js';

const require = createRequire(new URL('../Client/package.json', import.meta.url));
// Use the browser bundle, matching the app rather than vendor Node entry points.
const {outputFiles} = await require('esbuild').build({
  stdin: {contents: "export {ScatterplotLayer, PathLayer, IconLayer, TextLayer} from '@deck.gl/layers';", resolveDir: new URL('../Client/', import.meta.url).pathname},
  bundle: true, platform: 'browser', format: 'esm', write: false,
});
const vendor = await import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text).toString('base64')}`);
const sourceUrl = new URL('../Client/Scripts/fleetMap/rendering/scene.js', import.meta.url);
const layerSourceUrl = new URL('../Client/Scripts/fleetMap/rendering/sceneLayers.js', import.meta.url);
const controlLayers = (await readFile(layerSourceUrl, 'utf8'))
  .replace(/import \{\s*memoizeLast\s*\} from '[^']+';/, 'const memoizeLast = () => (_, create) => create();')
  .replaceAll(/from '(\.\/[^']+)'/g, (_, path) => `from '${new URL(path, layerSourceUrl).href}'`);
const controlLayerUrl = `data:text/javascript;base64,${Buffer.from(controlLayers).toString('base64')}`;
let controlSource = await readFile(sourceUrl, 'utf8');
controlSource = controlSource.replace("'./sceneLayers.js'", `'${controlLayerUrl}'`);
controlSource = controlSource.replaceAll(/from '(\.\/[^']+)'/g, (_, path) => `from '${new URL(path, sourceUrl).href}'`)
  .replace(/import \{memoizeLast\} from '[^']+';/, 'const memoizeLast = () => (_, create) => create();')
  .replace('if (stopsDirty) {', 'if (true) {')
  .replace('snapshotStops(stops, stopData, distanceData)', 'snapshotStops(stops)');
const {createScene: uncachedScene} = await import(`data:text/javascript;base64,${Buffer.from(controlSource).toString('base64')}`);

function run(factory, frames) {
  let pending, counting = false, constructions = 0, dataChanges = 0, domReads = 0;
  const previous = new Map();
  globalThis.requestAnimationFrame = callback => { pending = callback; return 1; };
  globalThis.cancelAnimationFrame = () => { pending = null; };
  class Overlay {
    setMap() {}
    finalize() {}
    setProps({layers}) {
      for (const layer of layers) {
        if (counting && previous.get(layer.id) !== layer.props.data) dataChanges++;
        previous.set(layer.id, layer.props.data);
      }
    }
  }
  const dependencies = {GoogleMapsOverlay: Overlay};
  for (const name of ['ScatterplotLayer', 'PathLayer', 'IconLayer', 'TextLayer']) {
    dependencies[name] = class extends vendor[name] {
      constructor(props) { super(props); if (counting) constructions++; }
    };
  }
  const map = {getDiv: () => ({dataset: {}}), setOptions() {}, addListener: () => ({remove() {}})};
  const scene = factory(map, dependencies);
  const stations = scene.createStationPointLayer(map, () => {});
  for (let i = 0; i < 1000; i++) stations.setPoint(String(i), {lng: -120 + i * .04, lat: 35}, 'rgb(30,140,70)', false, false);
  stations.setVisible(true);
  for (let i = 0; i < 2; i++) {
    const stop = new scene.StopMarker({position: {lng: -80 + i, lat: 35}, number: String(i + 1)});
    stop.setDistance('100 mi · 161 km');
  }
  const route = new scene.Polyline({map, strokeWeight: 4, strokeColor: '#2e50e7'});
  route.setPath(Array.from({length: 2000}, (_, i) => ({lng: -80 + i * .0005, lat: 35})));
  const trucks = Array.from({length: 3}, (_, i) => {
    const truck = scene.createTruckMarker(map, () => {});
    truck.update({unitNumber: String(11000 + i), engineState: 'On'});
    return truck;
  });
  const flush = () => { const callback = pending; pending = null; callback?.(); };
  trucks.forEach((truck, i) => truck.render({longitude: -80 + i, latitude: 35, heading: 90}));
  flush(); counting = true;
  const start = performance.now();
  for (let frame = 1; frame <= frames; frame++) {
    trucks.forEach((truck, i) => truck.render({longitude: -80 + i + frame * .000001, latitude: 35, heading: 90}));
    flush();
  }
  const cpuMs = performance.now() - start;
  scene.dispose();
  return {cpuMs, constructions, dataChanges, domReads};
}
run(createScene, 3000); run(uncachedScene, 3000);
const frames = 30000, runs = {cached: [], uncached: []};
for (let i = 0; i < 7; i++) {
  for (const key of i % 2 ? ['uncached', 'cached'] : ['cached', 'uncached']) {
    runs[key].push(run(key === 'cached' ? createScene : uncachedScene, frames));
  }
}
const summary = Object.fromEntries(Object.entries(runs).map(([key, values]) => [key, {
  medianCpuMs: values.map(v => v.cpuMs).sort((a, b) => a - b)[3],
  minCpuMs: Math.min(...values.map(v => v.cpuMs)), maxCpuMs: Math.max(...values.map(v => v.cpuMs)),
  constructions: values[0].constructions, dataChanges: values[0].dataChanges, domReads: values[0].domReads,
}]));
console.log(JSON.stringify({scope: 'CPU layer preparation only; cache-disabled control, not historical application', frames, samples: 7,
  fixture: {trucks: 3, stations: 1000, stops: 2, routePoints: 2000}, ...summary,
  medianCpuReductionPercent: 100 * (1 - summary.cached.medianCpuMs / summary.uncached.medianCpuMs),
}, null, 2));
