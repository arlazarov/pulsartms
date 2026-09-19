import test from 'node:test';
import assert from 'node:assert/strict';
import { layoutTruckLabels } from '../../Scripts/fleetMap/rendering/truckLabelLayout.js';
import { markerProjection } from '../../Scripts/fleetMap/rendering/markerProjection.js';
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
      const rows = layoutTruckLabels(source, zoom);
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
  const rows = layoutTruckLabels(source, 17);
  assertSeparate(rows, 17);
  assert.deepEqual(
    rows.map(row => row.position),
    source.map(row => row.position),
  );
});

test('selected truck gets placement priority without losing any identity', () => {
  const source = [truck('11005'), { ...truck('54777'), selected: true }];
  const rows = layoutTruckLabels(source, 17);
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
  const initial = layoutTruckLabels(source, 17);
  const next = layoutTruckLabels([...source].reverse(), 17.25, initial);
  assert.deepEqual(
    next.map(row => row.labelOffset),
    [...initial].reverse().map(row => row.labelOffset),
  );
  assert.deepEqual(layoutTruckLabels([], 17, next), []);
  assert.deepEqual(layoutTruckLabels([source[0]], 17)[0].labelOffset, [0, -30]);
});

test('displaced truck labels reuse bounded geographic connectors', () => {
  const rows = layoutTruckLabels([truck('11005'), truck('54777')], 17);
  for (const row of rows) {
    const icon = markerAnchor(row.labelOffset);
    const [x, y] = row.labelOffset;
    assert.ok(decodeURIComponent(icon.url).includes(`M0 0 L${x} ${y}`));
    assert.equal(markerAnchor(row.labelOffset), icon);
  }
});
