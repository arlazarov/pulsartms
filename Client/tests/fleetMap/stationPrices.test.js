import test from 'node:test';
import assert from 'node:assert/strict';
import {
  selectStationPrices,
  comparisonPrice,
  priceStatistics,
} from '../../Scripts/fleetMap/stations/stationPrices.js';
import { createStationLayer } from '../../Scripts/fleetMap/stations/stationLayer.js';

const date = '2026-09-09';
const quote = (overrides = {}) => ({
  effectiveFrom: date,
  effectiveTo: date,
  currency: 'USD',
  unit: 'US gal',
  product: 'Diesel',
  discountPrice: 3,
  priceAfterIfta: 2.5,
  ...overrides,
});
const station = (overrides = {}) => ({
  id: 'one',
  latitude: 40,
  longitude: -79,
  discounts: [],
  ...overrides,
});

test('map displays the server-selected road diesel rather than a newer cheaper non-road product', () => {
  const road = quote({ effectiveFrom: '2026-09-08' });
  const discounts = [
    quote({ product: 'DEF', discountPrice: 0.1 }),
    quote({ product: 'Reefer Diesel', discountPrice: 0.2 }),
    road,
  ];
  const item = selectStationPrices(
    [station({ discounts, cashDiscount: road, iftaDiscount: road })],
    date,
  )[0];
  assert.equal(item.discount, road);
  assert.equal(item.station.discounts, discounts);
});

test('IFTA chooses its ready server quote and never calculates tax or converts Canadian units', () => {
  const cash = quote({
    currency: 'CAD',
    unit: 'L',
    discountPrice: 1.4,
    priceAfterIfta: null,
  });
  const net = quote({
    currency: 'CAD',
    unit: 'L',
    discountPrice: 1.5,
    priceAfterIfta: 1.1,
  });
  const data = [
    station({ discounts: [cash, net], cashDiscount: cash, iftaDiscount: net }),
  ];
  const direct = selectStationPrices(data, date, false)[0];
  const ifta = selectStationPrices(data, date, true)[0];
  assert.equal(direct.discount, cash);
  assert.equal(ifta.discount, net);
  assert.equal(ifta.discount.unit, 'L');
  assert.equal(comparisonPrice(ifta.discount, true), 1.1);
  assert.equal(priceStatistics([ifta], true).get('CAD').min, 1.1);
  assert.equal(selectStationPrices(data, date, false)[0].discount, cash);
});

test('ambiguous or legacy-unselected quotes keep a neutral station instead of an invalid cheapest price', () => {
  for (const selected of [{ cashDiscount: null, iftaDiscount: null }, {}]) {
    const item = selectStationPrices(
      [
        station({
          ...selected,
          discounts: [
            quote(),
            quote({ currency: 'CAD', unit: 'L', discountPrice: 1 }),
          ],
        }),
      ],
      date,
    )[0];
    assert.deepEqual(item.position, { lat: 40, lng: -79 });
    assert.equal(comparisonPrice(item.discount, false), null);
    assert.equal(comparisonPrice(item.discount, true), null);
    assert.equal(priceStatistics([item], false).size, 0);
  }
});

test('missing IFTA retains known cash details but an unavailable comparison', () => {
  const cash = quote({ priceAfterIfta: null });
  const item = selectStationPrices(
    [station({ discounts: [cash], cashDiscount: cash, iftaDiscount: null })],
    date,
    true,
  )[0];
  assert.equal(item.discount, cash);
  assert.equal(comparisonPrice(item.discount, true), null);
  for (const value of [0, -1, NaN, Infinity, null])
    assert.equal(comparisonPrice(quote({ priceAfterIfta: value }), true), null);
});

test('expired selections cannot replace a current quote and bad positions remain excluded', () => {
  const expired = quote({ effectiveTo: '2026-09-08' });
  const current = quote();
  const data = station({
    discounts: [current],
    cashDiscount: expired,
    iftaDiscount: expired,
  });
  assert.equal(
    comparisonPrice(selectStationPrices([data], date)[0].discount, false),
    null,
  );
  assert.equal(
    selectStationPrices([{ ...data, latitude: 100 }], date).length,
    0,
  );
  assert.equal(
    selectStationPrices(
      [station({ discounts: [expired], cashDiscount: expired })],
      date,
    ).length,
    0,
  );
});

test('IFTA toggles replace the selected popup quote without another station request', async t => {
  const previousDocument = globalThis.document,
    previousStyle = globalThis.getComputedStyle;
  const element = () => ({
    children: [],
    style: { setProperty() {} },
    classList: { add() {}, toggle() {} },
    append(...nodes) {
      this.children.push(...nodes);
    },
    replaceChildren(...nodes) {
      this.children = nodes;
    },
    setAttribute() {},
    addEventListener() {},
    removeEventListener() {},
    remove() {},
  });
  globalThis.document = { documentElement: {}, createElement: element };
  globalThis.getComputedStyle = () => ({ getPropertyValue: () => '10,20,30' });
  t.after(() => {
    globalThis.document = previousDocument;
    globalThis.getComputedStyle = previousStyle;
  });
  let select,
    popup,
    shown = 0,
    displayedPrice;
  const layer = createStationLayer(
    {},
    () => {},
    (_map, onSelect) => {
      select = onSelect;
      return {
        setPoint(...args) {
          displayedPrice = args[7];
        },
        removePoint() {},
        redraw() {},
        setVisible() {},
        dispose() {},
      };
    },
    () => ({
      show(content) {
        popup = content;
        shown++;
      },
      hide() {},
      dispose() {},
    }),
  );
  t.after(() => layer.dispose());
  const cash = quote({ discountPrice: 3, priceAfterIfta: null });
  const net = quote({ discountPrice: 3.2, priceAfterIfta: 2.5 });
  await layer.setStations(
    [
      station({
        discounts: [cash, net],
        cashDiscount: cash,
        iftaDiscount: net,
      }),
    ],
    date,
    false,
  );
  await layer.setVisible(true);
  select('one');
  const find = (node, name) =>
    node.className === name
      ? node
      : (node.children ?? []).map(child => find(child, name)).find(Boolean);
  const value = name => find(popup, `fleet-station-popup__${name}`).textContent;
  assert.equal(value('discount'), '3.000');
  assert.equal(value('ifta'), 'N/A');
  assert.equal(displayedPrice, 3);
  await layer.setIfta(true);
  assert.equal(value('discount'), '3.200');
  assert.equal(value('ifta'), '2.500');
  assert.equal(
    displayedPrice,
    2.5,
    'GPU circle receives the same server-selected IFTA comparison, not a client tax formula',
  );
  await layer.setIfta(false);
  assert.equal(value('discount'), '3.000');
  assert.equal(displayedPrice, 3);
  assert.equal(shown, 3);
});
