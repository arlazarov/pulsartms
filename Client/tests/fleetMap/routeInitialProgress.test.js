import test from 'node:test';
import assert from 'node:assert/strict';
import {createRouteLayer} from '../../Scripts/fleetMap/routes/routeLayer.js';

const point = longitude => ({latitude: 40, longitude});
const plan = () => ({id: 'route', version: 1, truckId: 'truck', dispatchId: 'dispatch', fromCurrentPosition: true,
  tracking: {nextStopId: 'delivery'}, stops: [{id: 'delivery', job: 'Delivery', point: point(-78)}],
  route: {legs: [{miles: 200, points: [point(-80), point(-79), point(-78)]}]}});

function fixture(t) {
  const previousGoogle = globalThis.google;
  const lines = [], writes = [], fits = [], notifications = [];
  const listeners = new Map(), removedListeners = [];
  let zoom = 5, following = false;
  globalThis.google = {maps: {
    LatLng: class {constructor(value) {Object.assign(this, value);}},
    LatLngBounds: class {points = []; extend(value) {this.points.push({...value});}},
  }};
  class Line {
    constructor() {this.path = []; lines.push(this);}
    setOptions() {}
    setPath(path) {this.path = path; writes.push(path.map(value => ({...value})));}
    getPath() {return {
      setAt: (index, value) => {this.path[index] = value; writes.push(this.path.map(p => ({...p})));},
      removeAt: index => {this.path.splice(index, 1); writes.push(this.path.map(p => ({...p})));},
    };}
    setMap() {}
  }
  class Marker {
    constructor(options) {Object.assign(this, options);}
    setNumber() {}
    setJob() {}
  }
  const layer = createRouteLayer({getZoom: () => zoom,
    fitBounds(bounds) {fits.push(bounds.points);},
    addListener(name, callback) {
      listeners.set(name, callback);
      return {remove() {listeners.delete(name); removedListeners.push(name);}};
    },
  }, (...values) => notifications.push(values), () => {}, Line, Marker,
  () => ({hide() {}, show() {}, dispose() {}}), () => !following);
  t.after(() => {layer.dispose(); globalThis.google = previousGoogle;});
  return {layer, lines, writes, fits, notifications, listeners, removedListeners,
    zoom(value) {zoom = value; listeners.get('idle')?.();},
    drag() {listeners.get('dragstart')?.();}, following(value) {following = value;}};
}

test('cold saved preview never draws or fits its driven prefix before authoritative progress arrives', t => {
  const f = fixture(t), saved = plan();
  f.layer.setPlan(saved, true);
  f.layer.setProgress(null);
  f.zoom(14);
  assert.ok(f.lines.every(line => line.path.length === 0));
  assert.equal(f.fits.length, 0);
  assert.deepEqual(f.notifications, []);

  f.layer.setPlan(structuredClone(saved), false);
  f.layer.setProgress({progressMiles: 150});
  assert.ok(f.writes.filter(path => path.length).every(path => path.every(p => p.lng >= -78.5)));
  assert.equal(f.fits.length, 1);
  assert.ok(f.fits[0].every(p => p.lng >= -78.5), 'first camera fit covers only the remaining road');
  assert.deepEqual(f.notifications, [['truck', 150, 50]]);
  f.layer.setProgress({progressMiles: 160});
  f.zoom(5);
  assert.equal(f.fits.length, 1, 'progress and zoom updates do not repeat the deferred fit');
});

test('atomic plan and progress update handles both cold and same-identity metadata payloads', t => {
  const f = fixture(t), saved = plan();
  f.layer.setPlan(saved, true, {progressMiles: 120});
  assert.equal(f.lines[0].path[0].lng, -78.8);
  assert.equal(f.fits.length, 1);
  f.layer.setPlan(structuredClone(saved), false, {progressMiles: 130});
  assert.equal(f.lines[0].path[0].lng, -78.7);
  assert.ok(f.writes.filter(path => path.length).every(path => path.every(p => p.lng >= -78.8)));
  assert.equal(f.fits.length, 1);
});

test('progressless warm polling and detail changes retain only the previously trimmed road', t => {
  const f = fixture(t), saved = plan();
  f.layer.setPlan(saved, false);
  f.layer.setProgress({progressMiles: 150});
  const paths = f.lines.map(line => line.path), notifications = f.notifications.length;
  f.layer.setPlan(structuredClone(saved), false);
  f.layer.setProgress(null);
  f.lines.forEach((line, i) => assert.equal(line.path, paths[i]));
  f.zoom(14);
  assert.ok(f.lines.flatMap(line => line.path).every(p => p.lng >= -78.5));
  assert.equal(f.notifications.length, notifications, 'zoom cannot report retained progress as a fresh measurement');
  assert.equal(f.fits.length, 0);
});

for (const change of ['truck', 'dispatch', 'geometry']) {
  test(`a ${change} change cannot inherit the earlier route progress or deferred fit`, t => {
    const f = fixture(t), old = plan(), replacement = plan();
    f.layer.setPlan(old, false);
    f.layer.setProgress({progressMiles: 150});
    f.layer.setPlan({...old, version: 2}, true);
    if (change === 'truck') replacement.truckId = 'another-truck';
    if (change === 'dispatch') replacement.dispatchId = 'another-dispatch';
    if (change === 'geometry') replacement.version = 3;
    f.layer.setPlan(replacement, false);
    f.layer.setProgress(null);
    f.zoom(14);
    assert.ok(f.lines.every(line => line.path.length === 0));
    assert.equal(f.fits.length, 0);
    f.layer.setProgress({progressMiles: 20});
    assert.equal(f.lines[0].path[0].lng, -79.8);
    assert.equal(f.fits.length, 0);
  });
}

test('following or disposal cancels deferred route fitting', t => {
  const f = fixture(t);
  f.layer.setPlan(plan(), true);
  f.layer.setProgress(null);
  f.following(true);
  f.layer.setProgress({progressMiles: 100});
  f.following(false);
  f.layer.setProgress({progressMiles: 110});
  assert.equal(f.fits.length, 0);
  f.layer.setPlan({...plan(), version: 2}, true);
  f.layer.dispose();
  f.layer.setProgress({progressMiles: 120});
  assert.equal(f.fits.length, 0);
});

test('manual dragging cancels the pending initial fit without suppressing route progress', t => {
  const f = fixture(t), saved = plan();
  f.layer.setPlan(saved, true, null);
  f.drag();
  f.layer.setPlan(structuredClone(saved), false, {progressMiles: 150});
  f.zoom(14);
  f.layer.setProgress({progressMiles: 160});
  assert.equal(f.fits.length, 0, 'late progress cannot override the manually positioned camera');
  assert.ok(f.lines.flatMap(line => line.path).every(p => p.lng >= -78.4));
  assert.deepEqual(f.notifications, [['truck', 150, 50]]);

  f.layer.setPlan(structuredClone(saved), true, {progressMiles: 160});
  assert.equal(f.fits.length, 1, 'a later explicit fit request still works');
});

test('disposal removes the drag and detail listeners exactly once', t => {
  const f = fixture(t);
  assert.deepEqual([...f.listeners.keys()].sort(), ['dragstart', 'idle']);
  f.layer.dispose();
  f.layer.dispose();
  assert.equal(f.listeners.size, 0);
  assert.deepEqual(f.removedListeners.sort(), ['dragstart', 'idle']);
});
