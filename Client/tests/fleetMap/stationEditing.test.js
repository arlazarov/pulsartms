import test from 'node:test';
import assert from 'node:assert/strict';
import { createStationLayer } from '../../Scripts/fleetMap/stations/stationLayer.js';

const truckId = '10000000-0000-0000-0000-000000000001';
const dispatchId = '10000000-0000-0000-0000-000000000002';
const first = '20000000-0000-0000-0000-000000000001';
const second = '20000000-0000-0000-0000-000000000002';
const missing = '20000000-0000-0000-0000-000000000003';
const selection = (stationId = first) => ({
  truckId,
  dispatchId,
  stationId,
  point: { latitude: 41, longitude: -80 },
  yourPrice: 3,
  economicPrice: 2.5,
  currency: 'USD',
  unit: 'US gal',
});

function fixture(t) {
  const originalDocument = globalThis.document,
    originalStyle = globalThis.getComputedStyle;
  t.after(() => {
    globalThis.document = originalDocument;
    globalThis.getComputedStyle = originalStyle;
  });
  const element = () => ({
    children: [],
    style: { setProperty() {} },
    classList: {
      add() {},
      toggle() {},
      contains() {
        return false;
      },
    },
    setAttribute() {},
    append(...nodes) {
      this.children.push(...nodes);
    },
    replaceChildren(...nodes) {
      this.children = nodes;
    },
    addEventListener() {},
    removeEventListener() {},
    remove() {},
  });
  globalThis.document = { documentElement: {}, createElement: element };
  globalThis.getComputedStyle = () => ({
    getPropertyValue: property =>
      ({
        '--clr-success-700-rgb': '10, 150, 10',
        '--clr-warning-500-rgb': '220, 180, 0',
        '--clr-danger-700-rgb': '220, 50, 10',
        '--clr-neutral-500-rgb': '100, 110, 120',
      })[property],
  });
  const points = new Map();
  let visible = false;
  let selectPoint,
    popupContent,
    popupVisible = false;
  const layer = createStationLayer(
    {},
    undefined,
    (_, onSelect) => {
      selectPoint = onSelect;
      return {
        setPoint(id, position, color, recommended, selected, numbers, editing) {
          points.set(id, {
            id,
            position,
            color,
            recommended,
            selected,
            numbers,
            editing,
          });
        },
        removePoint(id) {
          points.delete(id);
        },
        redraw() {},
        setVisible(value) {
          visible = value;
        },
        hitTest() {},
        dispose() {
          points.clear();
        },
      };
    },
    () => ({
      hide() {
        popupVisible = false;
      },
      show(content) {
        popupContent = content;
        popupVisible = true;
      },
      dispose() {},
    }),
  );
  t.after(() => layer.dispose());
  layer.setEditContext({ truckId, dispatchId });
  const stations = [first, second].map((id, index) => {
    const quote = {
      currency: 'USD',
      unit: 'US gal',
      discountPrice: 3 + index,
      priceAfterIfta: 2.5 + index,
      effectiveFrom: '2026-09-09',
      effectiveTo: '2026-09-09',
    };
    return {
      id,
      name: id,
      latitude: 40,
      longitude: -79 + index,
      cashDiscount: quote,
      iftaDiscount: quote,
    };
  });
  return {
    layer,
    points,
    stations,
    visible: () => visible,
    select: id => selectPoint(id),
    popup: () => ({ visible: popupVisible, content: popupContent }),
  };
}

test('editing focuses known station coordinates, preserves price color and survives visibility and recommendation changes', async t => {
  const { layer, points, stations, visible } = fixture(t);
  await layer.setStations(stations, '2026-09-09', false);
  await layer.setVisible(true);
  const color = points.get(first).color;
  assert.deepEqual(layer.setEditing(selection()), { lat: 40, lng: -79 });
  assert.equal(points.get(first).color, color);
  assert.equal(points.get(first).editing, true);
  await layer.setVisible(false);
  await layer.setRecommended([]);
  assert.equal(
    points.get(first).color,
    color,
    'hiding ordinary stations preserves the edited price color',
  );
  assert.equal(
    visible(),
    false,
    'editing does not toggle the complete station layer',
  );
  assert.equal(points.get(first).editing, true);
  assert.equal(points.get(second).editing, false);
  layer.setEditing(selection(second));
  assert.equal(points.get(first).editing, false);
  assert.equal(points.get(second).editing, true);
  layer.setEditing(null);
  assert.equal(
    [...points.values()].some(point => point.editing),
    false,
  );
});

test('only one temporary edited station is retained when price markers were not loaded', async t => {
  const { layer, points } = fixture(t);
  assert.deepEqual(layer.setEditing(selection()), { lat: 41, lng: -80 });
  assert.equal(points.size, 1);
  assert.equal(points.get(first).editing, true);
  layer.setEditing(selection(second));
  assert.deepEqual([...points.keys()], [second]);
  await layer.setRecommended([]);
  assert.equal(points.get(second).editing, true);
  layer.setEditing(null);
  assert.equal(points.size, 0);
});

test('planned station colors survive hidden fuel layer, quote refresh, selection and IFTA changes', async t => {
  const { layer, points, stations, select } = fixture(t);
  const low = '10, 150, 10';
  await layer.setRecommended([
    {
      id: first,
      point: { latitude: 40, longitude: -79 },
      yourPrice: 3,
      currency: 'USD',
      unit: 'US gal',
      numbers: '1',
    },
  ]);
  assert.equal(
    points.get(first).color,
    '100, 110, 120',
    'a saved purchase alone cannot establish the global price color',
  );
  await layer.setStations(stations, '2026-09-09', false);
  assert.equal(points.get(first).color, low);
  await layer.setVisible(true);
  assert.equal(points.get(first).color, '10, 150, 10');
  await layer.setVisible(false);
  assert.equal(points.get(first).color, low);
  select(first);
  assert.equal(points.get(first).color, low);
  await layer.setIfta(true);
  assert.equal(points.get(first).color, low);
  await layer.setVisible(true);
  assert.equal(points.get(first).color, '10, 150, 10');
});

test('cold planned colors wait for a global price reference without fabricating cash or IFTA quotes', async t => {
  const { layer, points, visible } = fixture(t);
  const plan = (id, price) => ({
    id,
    point: { latitude: 40, longitude: -79 },
    yourPrice: price,
    currency: 'USD',
    unit: 'US gal',
    numbers: '1',
  });
  await layer.setRecommended([
    plan(first, 3),
    plan(second, 4),
    plan(missing, null),
  ]);
  assert.ok(
    [...points.values()].every(point => point.color === '100, 110, 120'),
  );
  await layer.setPriceOverview(
    [
      { id: first, currency: 'USD', cashPrice: 3, iftaPrice: null },
      { id: second, currency: 'USD', cashPrice: 4, iftaPrice: null },
    ],
    '2026-09-09',
    false,
  );
  assert.equal(visible(), false);
  assert.equal(points.get(first).color, '10, 150, 10');
  assert.equal(points.get(second).color, '220, 50, 10');
  assert.equal(points.get(missing).color, '100, 110, 120');
  layer.setEditing(selection());
  assert.equal(
    points.get(first).color,
    '10, 150, 10',
    'editing keeps the planned comparison scale',
  );
  await layer.setVisible(true);
  await layer.setVisible(false);
  assert.equal(points.get(first).color, '10, 150, 10');
  await layer.setIfta(true);
  assert.ok(
    [...points.values()].every(point => point.color === '100, 110, 120'),
  );
  await layer.setIfta(false);
  assert.equal(points.get(second).color, '220, 50, 10');
});

test('the dearer remaining purchase keeps its global cheap color before and after loading the catalog', async t => {
  const { layer, points, stations, select } = fixture(t);
  const catalog = Array.from({ length: 10 }, (_, index) => ({
    ...stations[0],
    id: index === 0 ? first : index === 1 ? second : `reference-${index}`,
    cashDiscount: {
      ...stations[0].cashDiscount,
      discountPrice: 3 + index / 10,
    },
    iftaDiscount: {
      ...stations[0].iftaDiscount,
      priceAfterIfta: 2.5 + index / 10,
    },
  }));
  await layer.setPriceOverview(
    catalog.map(station => ({
      id: station.id,
      currency: 'USD',
      cashPrice: station.cashDiscount.discountPrice,
      iftaPrice: station.iftaDiscount.priceAfterIfta,
    })),
    '2026-09-09',
    false,
  );
  const plan = id => ({
    id,
    point: { latitude: 40, longitude: -79 },
    yourPrice: 99,
    currency: 'USD',
    unit: 'US gal',
    numbers: id === first ? '2' : '3',
  });
  await layer.setRecommended([plan(first), plan(second)]);
  const color = points.get(second).color;
  const [red, green] = color.split(',').map(Number);
  assert.ok(
    green > red,
    'a globally cheap station is green, not the red maximum of two purchases',
  );
  select(second);
  assert.equal(points.get(second).color, color);
  await layer.setRecommended([plan(second)]);
  assert.equal(
    points.get(second).color,
    color,
    'passing another purchase does not change the reference population',
  );
  layer.setEditing(selection(second));
  assert.equal(
    points.get(second).color,
    color,
    'editing uses the same global reference',
  );
  await layer.setStations(catalog, '2026-09-09', false);
  await layer.setVisible(true);
  await layer.setVisible(false);
  assert.equal(
    points.get(second).color,
    color,
    'loading detailed quotes cannot reclassify the same price',
  );
  await layer.setIfta(true);
  assert.equal(
    points.get(second).color,
    color,
    'IFTA uses its own complete currency scale',
  );
  await layer.setPriceOverview(null, '2026-09-10', true);
  assert.equal(
    points.get(second).color,
    '100, 110, 120',
    "a new date never uses yesterday's scale",
  );
});

test('planned fuel renders from server metadata without loading ordinary stations and clears on invalidation', async t => {
  const { layer, points, visible, select, popup } = fixture(t);
  const plan = {
    id: first,
    point: { latitude: 40, longitude: -79 },
    name: 'Planned station',
    address: '10 Truck Road',
    yourPrice: 3.5,
    currency: 'USD',
    unit: 'US gal',
    gallons: 116,
    purchaseCostUsd: 406,
    miles: 50,
    numbers: '1',
  };
  await layer.setRecommended([plan]);
  assert.equal(visible(), false);
  assert.equal(points.size, 1);
  assert.deepEqual(points.get(first).position, { lat: 40, lng: -79 });
  assert.equal(points.get(first).recommended, true);
  select(first);
  assert.equal(popup().visible, true);
  await layer.setRecommended([]);
  assert.equal(points.size, 0);
  assert.equal(popup().visible, false);
});

test('wrong identity cannot move the highlight and accepted identity changes clear temporary state', t => {
  const { layer, points } = fixture(t);
  layer.setEditing(selection());
  assert.equal(
    layer.setEditing({ ...selection(second), truckId: missing }),
    null,
  );
  assert.equal(points.get(first).editing, true);
  layer.setEditContext({ truckId: missing, dispatchId });
  assert.equal(points.size, 0);
  assert.equal(layer.setEditing(selection()), null);
  layer.setEditContext({ truckId, dispatchId });
  layer.setEditing(selection());
  layer.setEditContext(null);
  assert.equal(points.size, 0);
});

test('invalid locations clear obsolete highlighting and late disposed calls cannot retain points', t => {
  const { layer, points } = fixture(t);
  layer.setEditing(selection());
  assert.equal(
    layer.setEditing({
      ...selection(second),
      point: { latitude: 0, longitude: 0 },
    }),
    null,
  );
  assert.equal(points.size, 0);
  layer.setEditing(selection());
  layer.dispose();
  assert.equal(layer.setEditing(selection(second)), null);
  layer.setEditContext({ truckId, dispatchId });
  assert.equal(points.size, 0);
});

test('a pending station batch cannot replace a newly selected editing point', async t => {
  const { layer, points, stations } = fixture(t);
  await layer.setVisible(true);
  const pending = layer.setStations(
    Array.from({ length: 65 }, (_, index) => ({
      ...stations[0],
      id: `station-${index}`,
    })),
    '2026-09-09',
    false,
  );
  layer.setEditing(selection());
  await pending;
  assert.equal(points.get(first).editing, true);
  assert.equal(
    points.size,
    66,
    'editor focus does not cancel the normal station batch',
  );
  layer.setEditing(null);
  assert.equal(points.has(first), false);
});

test('clearing during a pending station batch cannot resurrect its temporary editing point', async t => {
  const { layer, points, stations } = fixture(t);
  await layer.setVisible(true);
  layer.setEditing(selection());
  const pending = layer.setStations(
    Array.from({ length: 65 }, (_, index) => ({
      ...stations[0],
      id: `station-${index}`,
    })),
    '2026-09-09',
    false,
  );
  layer.setEditing(null);
  await pending;
  assert.equal(points.has(first), false);
  assert.equal(points.size, 65);
});

test('clearing a known edited station closes its hidden-layer popup and later batches cannot reopen it', async t => {
  const { layer, stations, select, popup } = fixture(t);
  await layer.setStations(stations, '2026-09-09', false);
  layer.setEditing(selection());
  select(first);
  assert.equal(popup().visible, true);
  layer.setEditing(null);
  assert.equal(popup().visible, false);
  await layer.setRecommended([]);
  await layer.setStations(stations, '2026-09-09', false);
  assert.equal(popup().visible, false);
});

test('clearing edit focus keeps ordinary station inspection available when its layer is visible', async t => {
  const { layer, stations, select, popup } = fixture(t);
  await layer.setStations(stations, '2026-09-09', false);
  await layer.setVisible(true);
  layer.setEditing(selection());
  select(first);
  layer.setEditing(null);
  assert.equal(popup().visible, true);
});

test('temporary station details do not present the optimization price as an actual IFTA quote', async t => {
  const { layer, stations, select, popup } = fixture(t);
  const flatten = element => [element, ...element.children.flatMap(flatten)];
  const value = name =>
    flatten(popup().content).find(
      node => node.className === `fleet-station-popup__${name}`,
    ).textContent;
  layer.setEditing(selection());
  select(first);
  assert.equal(value('discount'), '3.000');
  assert.equal(value('ifta'), 'N/A');
  await layer.setIfta(true);
  assert.equal(value('ifta'), 'N/A');
  await layer.setStations(stations, '2026-09-09', true);
  assert.equal(
    value('ifta'),
    '2.500',
    'a genuine loaded quote replaces the unavailable fallback',
  );
});
