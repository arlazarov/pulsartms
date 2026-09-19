import test from 'node:test';
import assert from 'node:assert/strict';
import { createPriceComparison } from '../../Scripts/fleetMap/stations/stationPriceComparison.js';
import { selectStationPrices } from '../../Scripts/fleetMap/stations/stationPrices.js';

test('comparison renders server values and signed differences without recalculating money', t => {
  const previous = globalThis.document;
  globalThis.document = {
    createElement: tag => ({
      tag,
      children: [],
      append(...nodes) {
        this.children.push(...nodes);
      },
    }),
  };
  t.after(() => {
    globalThis.document = previous;
  });
  const quote = {
    currency: 'USD',
    unit: 'US gal',
    retailPrice: 5.5,
    discountPrice: 5,
    priceAfterIfta: null,
    savings: 0.5,
  };
  const table = createPriceComparison(
    {
      ...quote,
      comparison: {
        date: '2026-09-09',
        nextDate: '2026-09-10',
        next: { ...quote, discountPrice: 4.8 },
        retailChange: 0,
        discountChange: -0.2,
        iftaChange: null,
        savingsChange: 0.1,
        discountChangePercent: -4,
        savingsChangePercent: 20,
      },
    },
    new Date(2026, 8, 10),
  );
  assert.deepEqual(
    table.children[1].children[0].children.map(x => x.textContent),
    ['Price', 'Yesterday', 'Today', 'Change', 'Change %'],
  );
  assert.deepEqual(
    table.children[2].children[1].children.map(x => x.textContent),
    ['Your price', '5.000', '4.800', '-0.200', '-4.00%'],
  );
  assert.equal(table.children[2].children[2].children[3].textContent, '—');
  assert.equal(table.children[2].children[3].children[3].textContent, '+0.100');
  assert.equal(
    table.children[2].children[3].children[4].textContent,
    '+20.00%',
  );
  assert.equal(table.children[2].children[2].children[4].textContent, '—');
  const todayTable = createPriceComparison(
    {
      ...quote,
      comparison: { date: '2026-09-10', nextDate: '2026-09-11', next: quote },
    },
    new Date(2026, 8, 10),
  );
  assert.deepEqual(
    todayTable.children[1].children[0].children
      .slice(1, 3)
      .map(x => x.textContent),
    ['Today', 'Tomorrow'],
  );
  assert.equal(
    table.children[2].children[1].children[0].className,
    'fleet-station-popup__discount-label',
  );
  assert.equal(
    table.children[2].children[1].children[1].className,
    'fleet-station-popup__discount',
  );
  assert.equal(
    table.children[2].children[2].children[1].className,
    'fleet-station-popup__ifta',
  );
  assert.equal(
    table.children[2].children[3].children[1].className,
    'fleet-station-popup__savings',
  );
  assert.equal(createPriceComparison(quote), null);
});

test('map switches comparison with its cash or IFTA basis and never keeps another dates comparison', () => {
  const quote = {
    effectiveFrom: '2026-09-09',
    effectiveTo: '2026-09-10',
    discountPrice: 5,
  };
  const station = {
    latitude: 40,
    longitude: -80,
    discounts: [quote],
    cashDiscount: quote,
    iftaDiscount: quote,
    cashComparison: { date: '2026-09-09', discountChange: -0.1 },
    iftaComparison: { date: '2026-09-09', discountChange: 0.2 },
  };
  assert.equal(
    selectStationPrices([station], '2026-09-09')[0].discount.comparison
      .discountChange,
    -0.1,
  );
  assert.equal(
    selectStationPrices([station], '2026-09-09', true)[0].discount.comparison
      .discountChange,
    0.2,
  );
  assert.equal(
    selectStationPrices([station], '2026-09-10')[0].discount.comparison,
    undefined,
  );
});

test('next-day quotes use comparison tones instead of repeating baseline price accents', t => {
  const previous = globalThis.document;
  globalThis.document = {
    createElement: tag => ({
      tag,
      children: [],
      append(...nodes) {
        this.children.push(...nodes);
      },
    }),
  };
  t.after(() => {
    globalThis.document = previous;
  });
  const quote = {
    currency: 'USD',
    unit: 'US gal',
    retailPrice: 5,
    discountPrice: 4,
    priceAfterIfta: 3,
    savings: 1,
  };
  for (const change of [-1, 0, 1, null]) {
    const comparison = {
      date: '2026-09-10',
      nextDate: '2026-09-11',
      next: quote,
      retailChange: change,
      discountChange: change,
      iftaChange: change,
      savingsChange: change,
    };
    const rows = createPriceComparison(
      { ...quote, comparison },
      new Date(2026, 8, 10),
    ).children[2].children;
    for (const [index, row] of rows.entries()) {
      const next = row.children[2];
      const tone =
        change > 0 ? ' is-increase' : change < 0 ? ' is-decrease' : '';
      assert.equal(
        next.className,
        `fleet-station-popup__change${tone}${index === 3 ? ' is-savings' : ''}`,
      );
      assert.equal(next.className, row.children[3].className);
      assert.equal(next.className, row.children[4].className);
      assert.notEqual(next.className, row.children[1].className);
    }
  }
});
