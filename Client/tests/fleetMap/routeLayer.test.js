import { popupFixture } from './popupFixture.js';
import test from 'node:test';
import assert from 'node:assert/strict';
import { createRouteLayer } from '../../Scripts/fleetMap/routes/routeLayer.ts';

const textValues = node =>
  [node.textContent, ...(node.children ?? []).flatMap(textValues)].filter(
    Boolean,
  );

function zoomFixture(t, legs, initialZoom = 5) {
  const previousGoogle = globalThis.google;
  globalThis.google = {
    maps: {
      LatLng: class {
        constructor(point) {
          Object.assign(this, point);
        }
      },
    },
  };
  const lines = [],
    markers = [];
  let zoom = initialZoom,
    idle,
    pathWrites = 0,
    styleWrites = 0;
  class Line {
    constructor(options) {
      Object.assign(this, options);
      this.path = [];
      lines.push(this);
    }
    setOptions(options) {
      Object.assign(this, options);
      styleWrites++;
    }
    setPath(path) {
      this.path = path;
      pathWrites++;
    }
    getPath() {
      return {
        setAt: (index, point) => {
          this.path[index] = point;
          pathWrites++;
        },
        removeAt: index => {
          this.path.splice(index, 1);
          pathWrites++;
        },
      };
    }
    setMap(map) {
      this.map = map;
    }
  }
  class Marker {
    constructor(options) {
      Object.assign(this, options);
      markers.push(this);
    }
    setNumber(number) {
      this.number = number;
    }
    setJob(job) {
      this.job = job;
    }
    setDistance() {}
  }
  const layer = createRouteLayer(
    {
      getZoom: () => zoom,
      addListener(name, callback) {
        if (name === 'idle') idle = callback;
        return { remove() {} };
      },
    },
    () => {},
    () => {},
    Line,
    Marker,
    () => ({ hide() {}, show() {}, dispose() {} }),
  );
  const plan = {
    id: 'zoom-plan',
    truckId: 'truck',
    version: 1,
    fromCurrentPosition: true,
    tracking: { nextStopId: 'stop-0' },
    route: { legs },
    stops: legs.map((leg, index) => ({
      id: `stop-${index}`,
      point: leg.points.at(-1),
      job: 'Delivery',
    })),
  };
  layer.setPlan(plan, false);
  t.after(() => {
    layer.dispose();
    globalThis.google = previousGoogle;
  });
  return {
    layer,
    lines,
    markers,
    plan,
    atZoom(value) {
      zoom = value;
      idle();
    },
    get pathWrites() {
      return pathWrites;
    },
    get styleWrites() {
      return styleWrites;
    },
  };
}

test('full-detail zoom changes retain dense route buffers without rewriting progress', t => {
  const points = Array.from({ length: 10001 }, (_, index) => ({
    latitude: 40,
    longitude: -80 + index / 10000,
  }));
  const fixture = zoomFixture(t, [{ miles: 100, points }], 14);
  fixture.layer.setProgress({ progressMiles: 25 });
  const originalPaths = fixture.lines.map(line => line.path);
  const writes = fixture.pathWrites;
  for (const zoom of [14.25, 15, 16.75, 20, 14]) {
    fixture.atZoom(zoom);
    fixture.lines.forEach((line, index) =>
      assert.equal(line.path, originalPaths[index]),
    );
    assert.equal(
      fixture.pathWrites,
      writes,
      `zoom ${zoom} must not rebuild full detail`,
    );
  }
  assert.ok(Math.abs(fixture.lines[0].path[0].lng + 79.75) < 1e-10);
  assert.deepEqual(fixture.lines[2].path.at(-1), { lat: 40, lng: -79 });
  fixture.layer.setProgress({ progressMiles: 25.001 });
  assert.ok(
    fixture.pathWrites > writes,
    'normal animated progress remains enabled',
  );
});

test('unchanged simplified indices preserve buffers while width thresholds remain independent', t => {
  const points = Array.from({ length: 1001 }, (_, index) => ({
    latitude: 40,
    longitude: -80 + index / 1000,
  }));
  const fixture = zoomFixture(t, [
    { miles: 50, points: points.slice(0, 501) },
    { miles: 50, points: points.slice(500) },
  ]);
  fixture.layer.setProgress({ progressMiles: 25 });
  const originalPaths = fixture.lines.map(line => line.path),
    markers = [...fixture.markers];
  const writes = fixture.pathWrites,
    styles = fixture.styleWrites;
  for (const [zoom, width] of [
    [6.5, 2],
    [7, 3],
    [9, 3],
    [10, 4],
    [13.8, 4],
    [5, 2],
  ]) {
    fixture.atZoom(zoom);
    assert.equal(
      fixture.pathWrites,
      writes,
      `zoom ${zoom} retains equivalent simplified geometry`,
    );
    fixture.lines.forEach((line, index) => {
      assert.equal(line.path, originalPaths[index]);
      assert.equal(line.strokeWeight, width);
    });
  }
  assert.equal(fixture.styleWrites, styles + 9);
  assert.deepEqual(fixture.lines[2].path, [
    { lat: 40, lng: -79.5 },
    { lat: 40, lng: -79 },
  ]);
  assert.deepEqual(fixture.markers, markers);
  assert.equal(points.length, 1001);
});

test('real detail changes retain the playback position, destination and mandatory stop anchors', t => {
  const fixture = zoomFixture(t, [
    {
      miles: 100,
      points: [
        { latitude: 40, longitude: -80 },
        { latitude: 40.0001, longitude: -79.5 },
        { latitude: 40, longitude: -79 },
      ],
    },
    {
      miles: 100,
      points: [
        { latitude: 40, longitude: -79 },
        { latitude: 40, longitude: -78 },
      ],
    },
  ]);
  fixture.layer.setProgress({ progressMiles: 25 });
  fixture.layer.setRenderedPosition(
    'truck',
    { latitude: 40.00004, longitude: -79.8 },
    true,
  );
  const leadingPoint = {
    lat: fixture.lines[0].path[0].lat,
    lng: fixture.lines[0].path[0].lng,
  };
  fixture.layer.setProgress({ progressMiles: 60 });
  const originalTail = fixture.lines[2].path,
    writes = fixture.pathWrites;
  fixture.atZoom(14);
  assert.ok(fixture.pathWrites > writes);
  assert.notEqual(fixture.lines[2].path, originalTail);
  assert.deepEqual(
    fixture.lines[0].path[0],
    leadingPoint,
    'zoom cannot replace playback with newer server progress',
  );
  assert.ok(fixture.lines[2].path.some(point => point.lng === -79.5));
  assert.ok(fixture.lines[2].path.some(point => point.lng === -79));
  assert.deepEqual(fixture.lines[2].path.at(-1), { lat: 40, lng: -78 });
  fixture.atZoom(5);
  assert.deepEqual(fixture.lines[0].path[0], leadingPoint);
  assert.deepEqual(fixture.lines[2].path, [
    { lat: 40, lng: -79 },
    { lat: 40, lng: -78 },
  ]);
});

test('unknown initial progress keeps the saved road visible across zoom detail changes', t => {
  const fixture = zoomFixture(t, [
    {
      miles: 100,
      points: [
        { latitude: 40, longitude: -80 },
        { latitude: 40, longitude: -79.5 },
        { latitude: 40, longitude: -79 },
      ],
    },
  ]);
  fixture.layer.setProgress({ progressMiles: null });
  const originalTail = fixture.lines[2].path,
    writes = fixture.pathWrites;
  fixture.atZoom(9);
  assert.equal(fixture.pathWrites, writes);
  assert.equal(fixture.lines[2].path, originalTail);
  assert.equal(fixture.lines[0].path[0].lng, -80);
  fixture.atZoom(14);
  assert.equal(fixture.lines[2].path.at(-1).lng, -79);
  assert.equal(fixture.lines[0].path[0].lng, -80);
});

test('route overlay preserves the viewport during updates and releases all map objects', () => {
  const lines = [];
  const markers = [];
  let styleWrites = 0;
  let infoWindow;
  let otherPopupVisible = true;
  globalThis.document = {
    createElement: () => ({
      style: {},
      children: [],
      setAttribute() {},
      listeners: {},
      addEventListener(name, fn) {
        this.listeners[name] = fn;
      },
      append(...children) {
        this.children.push(...children);
      },
      replaceChildren(...nodes) {
        this.children = nodes;
      },
      remove() {},
    }),
  };
  globalThis.google = {
    maps: {
      LatLng: class {
        constructor(value) {
          this.value = value;
        }
        lat() {
          return this.value.lat;
        }
        lng() {
          return this.value.lng;
        }
      },
      OverlayView: { preventMapHitsAndGesturesFrom() {} },
      InfoWindow: class {
        constructor(options) {
          Object.assign(this, options);
          infoWindow = this;
        }
        setContent(content) {
          this.content = content;
        }
        setPosition(position) {
          this.position = position;
        }
        open(options) {
          this.map = options.map;
        }
        close() {
          this.map = null;
        }
      },
      Polyline: class {
        constructor(options) {
          Object.assign(this, options);
          this.path = [];
          lines.push(this);
        }
        setOptions(options) {
          styleWrites++;
          Object.assign(this, options);
        }
        setPath(path) {
          this.path = path;
        }
        getPath() {
          return {
            removeAt: index => this.path.splice(index, 1),
            setAt: (index, value) => {
              assert.ok(
                value instanceof google.maps.LatLng,
                'MVCArray path updates require LatLng instances',
              );
              this.path[index] = { lat: value.lat(), lng: value.lng() };
            },
          };
        }
        setMap(map) {
          this.map = map;
        }
      },
      LatLngBounds: class {
        extend() {}
      },
      marker: {
        AdvancedMarkerElement: class {
          constructor(options) {
            Object.assign(this, options);
            this.listeners = {};
            this.distance = null;
            markers.push(this);
          }
          setDistance(value) {
            this.distance = value;
          }
        },
      },
    },
  };
  const reported = [];
  let fits = 0;
  let following = false;
  let zoom = 5;
  let onIdle;
  let listenerRemoved = false;
  const inset = { left: 55, top: 215, right: 455, bottom: 55 };
  const layer = createRouteLayer(
    {
      fitBounds(_bounds, padding) {
        fits++;
        assert.equal(padding, inset);
      },
      getZoom: () => zoom,
      addListener(name, callback) {
        if (name === 'idle') onIdle = callback;
        return {
          remove() {
            listenerRemoved = true;
          },
        };
      },
    },
    (...values) => reported.push(values),
    () => {
      otherPopupVisible = false;
    },
    google.maps.Polyline,
    google.maps.marker.AdvancedMarkerElement,
    popupFixture,
    () => !following,
    base => {
      assert.equal(base, 55);
      return inset;
    },
  );
  assert.equal(lines[0].strokeWeight, 2);
  zoom = 9;
  onIdle();
  assert.equal(lines[0].strokeWeight, 3);
  zoom = 12;
  onIdle();
  assert.equal(lines[0].strokeWeight, 4);
  zoom = 16;
  onIdle();
  assert.equal(lines[0].strokeWeight, 5);
  assert.equal(lines[1].strokeWeight, 5);
  const beforePanning = styleWrites;
  for (let i = 0; i < 20; i++) onIdle();
  assert.equal(
    styleWrites,
    beforePanning,
    'panning must not reapply unchanged route styling',
  );
  assert.equal(lines.length, 3);
  const plan = {
    id: 'load',
    truckId: 'truck',
    version: 1,
    stops: [
      {
        id: 'delivery',
        name: 'Delivery',
        point: { latitude: 40, longitude: -79 },
      },
    ],
    fromCurrentPosition: true,
    tracking: { nextStopId: 'delivery' },
    route: {
      legs: [
        {
          miles: 100,
          points: [
            { latitude: 40, longitude: -80 },
            { latitude: 40, longitude: -79 },
          ],
        },
      ],
    },
  };
  layer.setPlan(plan, true);
  layer.setProgress({ progressMiles: 50 });
  assert.equal(markers[0].distance, null);
  const setDistance = markers[0].setDistance;
  let labelWrites = 0;
  markers[0].setDistance = function (value) {
    labelWrites++;
    setDistance.call(this, value);
  };
  layer.setProgress({ progressMiles: 50.001 });
  assert.equal(
    labelWrites,
    0,
    'current-stop progress must not write persistent text labels',
  );
  layer.setProgress({ progressMiles: 50 });
  assert.deepEqual(reported.at(-1), ['truck', 50, 50]);
  assert.equal(typeof markers[0].onSelect, 'function');
  markers[0].onSelect();
  assert.deepEqual(infoWindow.position, { lat: 40, lng: -79 });
  assert.ok(infoWindow.map);
  layer.closePopup();
  assert.equal(infoWindow.map, null);
  assert.equal(
    infoWindow.content.children[0].className,
    'fleet-route-popup__location',
  );
  assert.equal(
    infoWindow.content.children[1].className,
    'fleet-route-popup__information',
  );
  assert.ok(
    infoWindow.content.children[0].children.some(
      node => node.textContent === 'Delivery',
    ),
  );
  assert.equal(lines[0].path[0].lng, -79.5);
  assert.ok(lines[1].path.length > 0);
  const originalPath = lines[0].path;
  const originalTail = lines[2].path;
  const tailSnapshot = structuredClone(originalTail);
  layer.setRenderedPosition('truck', { latitude: 40, longitude: -79.52 }, true);
  assert.equal(lines[0].path, originalPath);
  assert.ok(lines[1].path.length > 0);
  assert.equal(lines[0].path[0].lng, -79.52);
  assert.equal(lines[0].path.length, 2);
  assert.equal(lines[2].path, originalTail);
  assert.deepEqual(
    lines[2].path,
    tailSnapshot,
    'movement within a segment must leave the long route unchanged',
  );
  layer.setProgress({ progressMiles: 51 });
  assert.equal(lines[0].path, originalPath);
  assert.equal(
    lines[0].path[0].lng,
    -79.52,
    'fresh GPS progress must not jump ahead of animated truck',
  );
  zoom = 12;
  onIdle();
  assert.equal(lines[0].path[0].lng, -79.52);
  layer.setRenderedPosition('truck', { latitude: 40, longitude: -79.51 });
  layer.setRenderedPosition('truck', { latitude: 40, longitude: -79.51 }, true);
  assert.equal(lines[0].path[0].lng, -79.51);
  layer.setRenderedPosition('other', { latitude: 40, longitude: -79.51 }, true);
  assert.ok(lines[1].path.length > 0);
  layer.setRenderedPosition('truck', { latitude: 40, longitude: -79.5 }, true);
  const unchangedPath = lines[0].path;
  plan.fuelPlan = {
    calculatedAt: '2026-09-06T12:00:00Z',
    stops: [{ stationId: 'station-a' }],
  };
  layer.setPlan(plan, false);
  layer.setProgress({ progressMiles: 50 });
  assert.equal(lines[0].path, unchangedPath);
  assert.equal(
    markers.length,
    1,
    'fuel changes must not rebuild the route geometry or stop markers',
  );
  layer.setPlan(plan, true);
  layer.setProgress({ progressMiles: 60 });
  assert.equal(fits, 2);
  following = true;
  layer.setPlan(plan, true);
  assert.equal(fits, 2, 'late route fitting must not override active follow');
  assert.equal(markers.length, 1);
  assert.equal(
    lines[0].path[0].lng,
    -79.5,
    'refocusing must preserve playback progress',
  );
  layer.setProgress({ progressMiles: 60 }, false, true);
  assert.equal(lines[0].path[0].lng, -79.4);
  plan.version = 2;
  plan.fromCurrentPosition = false;
  plan.stops = [
    { id: 'pickup', point: { latitude: 40, longitude: -80 } },
    { id: 'middle', point: { latitude: 40, longitude: -79.5 } },
    { id: 'delivery', point: { latitude: 40, longitude: -79 } },
  ];
  plan.tracking = { nextStopId: 'middle' };
  plan.route.legs = [
    {
      miles: 50,
      points: [
        { latitude: 40, longitude: -80 },
        { latitude: 40, longitude: -79.5 },
      ],
    },
    {
      miles: 50,
      points: [
        { latitude: 40, longitude: -79.5 },
        { latitude: 40, longitude: -79 },
      ],
    },
  ];
  layer.setPlan(plan, true);
  assert.equal(fits, 2, 'new route geometry must also preserve follow camera');
  following = false;
  layer.setProgress({ progressMiles: 25 });
  assert.equal(lines[2].path.at(-1).lng, -79);
  assert.ok(lines[0].path.some(p => p.lng === -79.5));
  const nextLabel = markers.find(m => m.map && m.position.lng === -79.5);
  assert.equal(nextLabel.distance, null);
  nextLabel.onSelect();
  assert.ok(textValues(infoWindow.content).includes('25 mi · 40 km'));
  const deliveryMarker = markers.find(m => m.map && m.position.lng === -79);
  assert.equal(deliveryMarker.distance, null);
  deliveryMarker.onSelect();
  assert.ok(textValues(infoWindow.content).includes('75 mi · 121 km'));
  layer.closePopup();
  assert.deepEqual(reported.at(-1), ['truck', 25, 75]);
  assert.ok(lines[1].path.length > 0);
  const retainedLine = lines[0].path;
  layer.setProgress({ progressMiles: 75 });
  assert.equal(lines[0].path, retainedLine);
  assert.equal(lines[0].path[0].lng, -79.25);
  assert.equal(lines[0].path.at(-1).lng, -79);
  assert.equal(
    lines[0].path.some(p => p.lng === -79.5),
    false,
  );
  layer.setProgress({ progressMiles: null });
  assert.ok(lines[1].path.length > 0);
  assert.equal(nextLabel.distance, null);
  plan.tracking = { passedStopIds: ['delivery'] };
  layer.setPlan(plan, false);
  assert.equal(markers[0].map, null);
  assert.equal(fits, 2);
  layer.dispose();
  layer.dispose();
  layer.setPlan(plan, true);
  layer.setProgress({ progressMiles: 10 });
  layer.closePopup();
  assert.ok(lines.every(line => line.map === null));
  assert.equal(listenerRemoved, true);
  assert.ok(lines.every(x => x.map === null));
  assert.ok(markers.every(x => x.map === null));
});

test('same-version stop metadata and tracking update without replacing route geometry', () => {
  const lines = [],
    markers = [];
  let shown;
  globalThis.document = {
    createElement: () => ({
      children: [],
      append(...children) {
        this.children.push(...children);
      },
    }),
  };
  globalThis.google = {
    maps: {
      LatLng: class {
        constructor(point) {
          Object.assign(this, point);
        }
      },
    },
  };
  class Line {
    constructor() {
      this.path = [];
      lines.push(this);
    }
    setOptions() {}
    setPath(path) {
      this.path = path;
    }
    getPath() {
      return {
        setAt: (i, point) => (this.path[i] = point),
        removeAt: i => this.path.splice(i, 1),
      };
    }
    setMap() {}
  }
  class Marker {
    constructor(options) {
      this.distance = null;
      Object.assign(this, options);
      markers.push(this);
    }
    setNumber(value) {
      this.number = value;
    }
    setJob(value) {
      this.job = value;
    }
    setDistance(value) {
      this.distance = value;
    }
  }
  const layer = createRouteLayer(
    { getZoom: () => 5, addListener: () => ({ remove() {} }) },
    () => {},
    () => {},
    Line,
    Marker,
    () => ({
      hide() {
        shown = null;
      },
      show(content) {
        shown = content;
      },
      dispose() {},
    }),
  );
  const plan = {
    id: 'plan',
    truckId: 'truck',
    version: 1,
    fromCurrentPosition: true,
    stops: [
      {
        id: 'pickup',
        name: 'Old warehouse',
        job: 'Pickup',
        point: { latitude: 40, longitude: -79 },
      },
      {
        id: 'delivery',
        name: 'Customer',
        job: 'Delivery',
        point: { latitude: 40, longitude: -78 },
      },
    ],
    route: {
      legs: [
        {
          miles: 100,
          points: [
            { latitude: 40, longitude: -80 },
            { latitude: 40, longitude: -79 },
          ],
        },
        {
          miles: 100,
          points: [
            { latitude: 40, longitude: -79 },
            { latitude: 40, longitude: -78 },
          ],
        },
      ],
    },
    tracking: { nextStopId: 'pickup' },
  };
  layer.setPlan(plan, false);
  layer.setProgress({ progressMiles: 20 });
  const tail = lines[2].path;
  const retainedMarkers = [...markers];
  layer.setEtas(
    new Map([
      ['pickup', { text: 'ETA Sep 9, 09:00 AM local', tone: 'eta' }],
      ['delivery', { text: 'ETA Sep 10, 02:00 PM local', tone: 'success' }],
    ]),
  );
  assert.equal(markers[0].distance, null);
  assert.equal(markers[1].distance, null);
  markers[0].onSelect();
  assert.ok(
    shown.children[0].children.some(
      node => node.textContent === 'Old warehouse',
    ),
  );
  assert.ok(textValues(shown).includes('ETA Sep 9, 09:00 AM local'));
  assert.ok(textValues(shown).includes('80 mi · 129 km'));
  markers[1].onSelect();
  assert.ok(
    shown.children[0].children.some(node => node.textContent === 'Customer'),
  );
  assert.ok(textValues(shown).includes('ETA Sep 10, 02:00 PM local'));
  assert.ok(textValues(shown).includes('180 mi · 290 km'));
  assert.equal(lines[2].path, tail);
  assert.deepEqual(markers, retainedMarkers);
  layer.setEtas(new Map());
  assert.equal(markers[0].distance, null);
  assert.ok(textValues(shown).includes('—'));
  markers[0].onSelect();
  const updated = structuredClone(plan);
  Object.assign(updated.stops[0], {
    name: 'New warehouse',
    scheduledDate: '2026-09-09',
    scheduledTime: '15:30:00',
    notes: 'New instructions',
  });
  layer.setPlan(updated, false);
  layer.setProgress({ progressMiles: 20 });
  assert.equal(lines[2].path, tail);
  assert.equal(markers.length, 2);
  assert.equal(markers[0].distance, null);
  const content = textValues(shown);
  assert.ok(content.includes('New warehouse'));
  assert.ok(content.includes('Sep 9 · 03:30 PM'));
  assert.ok(!content.includes('New instructions'));
  updated.tracking = { nextStopId: 'delivery', passedStopIds: ['pickup'] };
  layer.setPlan(updated, false);
  assert.equal(lines[2].path, tail);
  assert.ok(markers[0].map);
  assert.equal(markers[1].number, '2');
  assert.equal(shown, null, 'passing the selected stop closes its details');
  layer.setEtas(
    new Map([
      ['delivery', { text: 'ETA Sep 10, 02:00 PM local', tone: 'success' }],
    ]),
    true,
  );
  markers[1].onSelect();
  const retainedPopup = shown;
  const beforeGeometryChange = lines[2].path;
  updated.version++;
  updated.route.legs[0].points.splice(1, 0, {
    latitude: 40.1,
    longitude: -79.5,
  });
  layer.setPlan(updated, false);
  layer.setProgress({ progressMiles: 20 });
  assert.equal(
    shown,
    retainedPopup,
    'safe geometry-version changes retain the open stop ETA popup',
  );
  assert.equal(
    markers.length,
    2,
    'unchanged stop identities retain their markers',
  );
  assert.notEqual(
    lines[2].path,
    beforeGeometryChange,
    'retained stop ETA does not pin the old road geometry',
  );
  updated.version++;
  updated.stops[1].point.longitude -= 0.1;
  layer.setPlan(updated, false);
  assert.equal(shown, null, 'a changed destination closes the old popup');
  layer.dispose();
});
