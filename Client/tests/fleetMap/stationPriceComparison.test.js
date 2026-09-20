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
  // Days run left to right, one column each; what a day's price did since
  // the day before is a small mark under that price.
  const cells = row =>
    row.children.map(cell =>
      cell.children.length
        ? cell.children.map(part => part.textContent).join(' ')
        : cell.textContent,
    );
  assert.deepEqual(
    table.children[1].children[0].children.map(x => x.textContent),
    ['Price', 'Yesterday', 'Today'],
  );
  assert.deepEqual(cells(table.children[2].children[1]), [
    'Your price',
    '5.000',
    '4.800 \u25bc0.200',
  ]);
  // The percentage is the mark's tooltip rather than a column of its own.
  assert.equal(
    table.children[2].children[1].children[2].children[1].title,
    '-4.00%',
  );
  assert.deepEqual(cells(table.children[2].children[2]), [
    'After IFTA',
    '\u2014',
    '\u2014',
  ]);
  assert.deepEqual(cells(table.children[2].children[3]), [
    'Savings',
    '0.500',
    '0.500 \u25b20.100',
  ]);
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
      // The price keeps its own accent; the tone belongs to the mark under
      // it, and a day that did not move carries no mark at all.
      const next = row.children[2];
      const mark = next.children[1];
      if (!change) {
        assert.equal(mark, undefined);
        continue;
      }
      assert.equal(
        mark.className,
        `fleet-station-popup__change${change > 0 ? ' is-increase' : ' is-decrease'}${index === 3 ? ' is-savings' : ''}`,
      );
      assert.equal(next.className, row.children[1].className);
    }
  }
});

// Whether to fuel now or wait is read off both sides of today. The day before
// used to be compared by opening the map on it and remembering the number.
test('yesterday, today and tomorrow stand in one row, each marked with what it did', t => {
  const previousDocument = globalThis.document;
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
    globalThis.document = previousDocument;
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
      previous: {
        date: '2026-09-09',
        nextDate: '2026-09-10',
        next: quote,
        retailChange: 0.1,
        discountChange: 0.25,
        iftaChange: null,
        savingsChange: -0.15,
        discountChangePercent: 5.26,
      },
      comparison: {
        date: '2026-09-10',
        nextDate: '2026-09-11',
        next: { ...quote, discountPrice: 4.8 },
        retailChange: 0,
        discountChange: -0.2,
        iftaChange: null,
        savingsChange: 0.2,
        discountChangePercent: -4,
      },
    },
    new Date(2026, 8, 10),
  );
  const cells = row =>
    row.children.map(cell =>
      cell.children.length
        ? cell.children.map(part => part.textContent).join(' ')
        : cell.textContent,
    );
  assert.deepEqual(
    table.children[1].children[0].children.map(x => x.textContent),
    ['Price', 'Yesterday', 'Today', 'Tomorrow'],
  );
  // Yesterday is what today was before it moved: 5.000 less the 0.250 rise.
  assert.deepEqual(cells(table.children[2].children[1]), [
    'Your price',
    '4.750',
    '5.000 \u25b20.250',
    '4.800 \u25bc0.200',
  ]);
  const [, , todayCell, tomorrowCell] = table.children[2].children[1].children;
  assert.match(todayCell.children[1].className, /is-increase/);
  assert.equal(todayCell.children[1].title, '+5.26%');
  assert.match(tomorrowCell.children[1].className, /is-decrease/);

  // Only the day before is known: the table still says what it can.
  const back = createPriceComparison(
    {
      ...quote,
      previous: {
        date: '2026-09-09',
        nextDate: '2026-09-10',
        next: quote,
        discountChange: 0.25,
      },
    },
    new Date(2026, 8, 10),
  );
  assert.deepEqual(
    back.children[1].children[0].children.map(x => x.textContent),
    ['Price', 'Yesterday', 'Today'],
  );
});

test('the day before belongs only to the date it ends on', () => {
  const quote = {
    effectiveFrom: '2026-09-09',
    effectiveTo: '2026-09-10',
    discountPrice: 5,
  };
  const station = {
    latitude: 40,
    longitude: -80,
    cashDiscount: quote,
    discounts: [quote],
    cashPreviousComparison: {
      date: '2026-09-09',
      nextDate: '2026-09-10',
      discountChange: 0.25,
    },
  };
  assert.equal(
    selectStationPrices([station], '2026-09-10')[0].discount.previous
      .discountChange,
    0.25,
  );
  assert.equal(
    selectStationPrices([station], '2026-09-09')[0].discount.previous,
    undefined,
  );
});
