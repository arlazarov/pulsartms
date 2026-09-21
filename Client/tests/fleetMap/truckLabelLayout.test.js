import test from 'node:test';
import assert from 'node:assert/strict';
import {
  layoutMapLabels,
  clusterText,
} from '../../Scripts/fleetMap/rendering/truckLabelLayout.js';
import { markerProjection } from '../../Scripts/fleetMap/rendering/markerProjection.ts';
import { markerAnchor } from '../../Scripts/fleetMap/rendering/markerAnchor.js';

const truck = (unit, dx = 0, dy = 0) => ({
  unit,
  position: [-80 + dx, 37 + dy],
  speed: 0,
  engine: 'off',
  onSelect() {},
});

function assertSeparate(rows, zoom) {
  const project = markerProjection(zoom);
  const boxes = rows.map(row => {
    const point = project(row.position);
    return point.map((value, axis) => value + row.labelOffset[axis]);
  });
  for (let i = 0; i < boxes.length; i++)
    for (let j = i + 1; j < boxes.length; j++)
      assert.ok(
        Math.abs(boxes[i][0] - boxes[j][0]) >= 60 ||
          Math.abs(boxes[i][1] - boxes[j][1]) >= 25,
        `${rows[i].unit} overlaps ${rows[j].unit}`,
      );
}

test('nearby 11005 and 54777 keep readable labels at close and fractional zoom', () => {
  for (const zoom of [12, 15, 17, 17.5, 19])
    for (const [dx, dy] of [
      [0, 0],
      [0.00005, 0.00003],
      [-0.00005, -0.00003],
    ]) {
      const source = [truck('11005'), truck('54777', dx, dy)];
      const rows = layoutMapLabels({ vehicles: source, zoom }).vehicles;
      assertSeparate(rows, zoom);
      for (let i = 0; i < source.length; i++) {
        assert.equal(rows[i].position, source[i].position);
        assert.equal(rows[i].onSelect, source[i].onSelect);
        assert.equal(source[i].labelOffset, undefined);
        assert.ok(Math.hypot(...rows[i].labelOffset) <= 75);
      }
    }
});

test('four coincident trucks retain separate labels without changing GPS', () => {
  const source = ['11005', '54777', '11006', '11007'].map(unit => truck(unit));
  const rows = layoutMapLabels({ vehicles: source, zoom: 17 }).vehicles;
  assertSeparate(rows, 17);
  assert.deepEqual(
    rows.map(row => row.position),
    source.map(row => row.position),
  );
});

test('selected truck gets placement priority without losing any identity', () => {
  const source = [truck('11005'), { ...truck('54777'), selected: true }];
  const rows = layoutMapLabels({ vehicles: source, zoom: 17 }).vehicles;
  assert.deepEqual(rows[1].labelOffset, [0, -30]);
  assertSeparate(rows, 17);
  assert.equal(rows[1].selected, true);
  assert.deepEqual(
    rows.map(row => row.unit),
    ['11005', '54777'],
  );
});

test('polling and reordered inputs retain clear offsets; removed trucks are evicted', () => {
  const source = [truck('11005'), truck('54777')];
  const initial = layoutMapLabels({ vehicles: source, zoom: 17 }).vehicles;
  const next = layoutMapLabels({
    vehicles: [...source].reverse(),
    zoom: 17.25,
    previous: initial,
  }).vehicles;
  assert.deepEqual(
    next.map(row => row.labelOffset),
    [...initial].reverse().map(row => row.labelOffset),
  );
  assert.deepEqual(
    layoutMapLabels({ vehicles: [], zoom: 17, previous: next }).vehicles,
    [],
  );
  assert.deepEqual(
    layoutMapLabels({ vehicles: [source[0]], zoom: 17 }).vehicles[0]
      .labelOffset,
    [0, -30],
  );
});

// The owner's report: with a truck selected and its next loads drawn, the
// "2 trucks" badge sat on a stop of the route being read - and offsetting it
// to escape only trailed a leader line across a state as the map zoomed out.
// It keeps its point and its wording, and the stops are drawn over it.
//
// A truck's own unit number stays put for the same reason: a truck standing
// on its delivery used to shove its number aside on a leader line.
test('a truck label steps off a cluster badge but never off a stop', () => {
  const zoom = 10;
  const project = markerProjection(zoom);
  const cluster = {
    count: 2,
    position: [-80, 37],
    members: [truck('11005'), truck('54777')],
  };
  const stop = { position: [-80, 37], markerOffsetX: 0, markerOffsetY: 0 };
  const near = { ...truck('11006', 0.01, 0), selected: true };

  const placed = layoutMapLabels({
    vehicles: [near],
    clusters: [cluster],
    zoom,
  });

  // The badge keeps its point; nothing is offset away from it.
  assert.equal(placed.clusters[0], cluster);
  assert.equal(clusterText(cluster), '2 trucks');
  const label = placed.vehicles[0].labelOffset;
  const [lx, ly] = project(near.position).map(
    (value, axis) => value + label[axis],
  );
  const [bx, by] = project(cluster.position);
  assert.ok(
    Math.abs(lx - bx) >= 30 + 17 || Math.abs(ly - by) >= 13 + 17,
    `label at ${lx},${ly} still covers the badge at ${bx},${by}`,
  );

  // A truck on top of a stop keeps its number above its own marker.
  const parked = layoutMapLabels({
    vehicles: [{ ...truck('11009'), selected: true }],
    zoom,
  });
  assert.deepEqual(parked.vehicles[0].labelOffset, [0, -30]);
  assert.equal(stop.position.length, 2, 'the stop itself is never moved');
});

test('displaced truck labels reuse bounded geographic connectors', () => {
  const rows = layoutMapLabels({
    vehicles: [truck('11005'), truck('54777')],
    zoom: 17,
  }).vehicles;
  for (const row of rows) {
    const icon = markerAnchor(row.labelOffset);
    const [x, y] = row.labelOffset;
    assert.ok(decodeURIComponent(icon.url).includes(`M0 0 L${x} ${y}`));
    assert.equal(markerAnchor(row.labelOffset), icon);
  }
});
