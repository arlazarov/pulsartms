import test from 'node:test';
import assert from 'node:assert/strict';
import { createPriceComparison } from '../../Scripts/fleetMap/stations/stationPriceComparison.ts';
import { selectStationPrices } from '../../Scripts/fleetMap/stations/stationPrices.ts';

const withDocument = t => {
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
};
const quote = {
  currency: 'USD',
  unit: 'US gal',
  retailPrice: 5.5,
  discountPrice: 5,
  priceAfterIfta: null,
  savings: 0.5,
};
const days = strip =>
  strip.children.map(cell => cell.children.map(part => part.textContent));

// Whether to fuel now or wait is read off both sides of today, and one price
// decides it - the one being paid. The days are a single line of that price
// under the list; a table of every price by day was a grid of rules with
// rows of two heights and a row of dashes.
test('the days are one line of the price being paid: yesterday, today, tomorrow', t => {
  withDocument(t);
  const strip = createPriceComparison(
    {
      ...quote,
      previous: {
        date: '2026-09-09',
        nextDate: '2026-09-10',
        next: quote,
        discountChange: 0.25,
        discountChangePercent: 5.26,
      },
      comparison: {
        date: '2026-09-10',
        nextDate: '2026-09-11',
        next: { ...quote, discountPrice: 4.8 },
        discountChange: -0.2,
        discountChangePercent: -4,
      },
    },
    new Date(2026, 8, 10),
  );
  assert.equal(strip.className, 'fleet-station-popup__days');
  // Yesterday is what today was before it moved: 5.000 less the 0.250 rise.
  assert.deepEqual(days(strip), [
    ['Yesterday', '4.750'],
    ['Today', '5.000', '\u25b2 0.250'],
    ['Tomorrow', '4.800', '\u25bc 0.200'],
  ]);
  const [, now, tomorrow] = strip.children;
  assert.match(now.className, /is-current/);
  assert.match(now.children[2].className, /is-increase/);
  assert.equal(now.children[2].title, '+5.26%');
  assert.match(tomorrow.children[2].className, /is-decrease/);
  // The first day of the line has nothing behind it to have moved from, so
  // it says nothing: on one line, on one baseline, that is a shorter phrase
  // and not a cell of another height.
  assert.equal(strip.children[0].children.length, 2);
});

test('the line says what it can when only one side of today is known', t => {
  withDocument(t);
  const forward = createPriceComparison(
    {
      ...quote,
      comparison: {
        date: '2026-09-10',
        nextDate: '2026-09-11',
        next: quote,
        discountChange: 0,
      },
    },
    new Date(2026, 8, 10),
  );
  assert.deepEqual(days(forward), [
    ['Today', '5.000'],
    ['Tomorrow', '5.000', 'same'],
  ]);
  const back = createPriceComparison(
    {
      ...quote,
      previous: {
        date: '2026-09-09',
        nextDate: '2026-09-10',
        next: quote,
        discountChange: -0.1,
      },
    },
    new Date(2026, 8, 10),
  );
  assert.deepEqual(days(back), [
    ['Yesterday', '5.100'],
    ['Today', '5.000', '\u25bc 0.100'],
  ]);
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
