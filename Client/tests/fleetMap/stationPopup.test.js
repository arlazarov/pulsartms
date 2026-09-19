import test from 'node:test';
import assert from 'node:assert/strict';
import { createStationPopup } from '../../Scripts/fleetMap/stations/stationPopup.js';
import { distanceLabel } from '../../Scripts/fleetMap/ui/distanceLabel.js';

test('planned and ordinary fuel distances follow units without changing purchases', t => {
  const previous = globalThis.document;
  const element = () => ({
    children: [],
    append(...nodes) {
      this.children.push(...nodes);
    },
    replaceChildren(...nodes) {
      this.children = nodes;
    },
    setAttribute() {},
    addEventListener() {},
    removeEventListener() {},
  });
  globalThis.document = { createElement: element, createElementNS: element };
  t.after(() => {
    globalThis.document = previous;
  });
  let unit = 'miles';
  const popup = createStationPopup(
    () => {},
    miles => distanceLabel(miles, unit),
  );
  const data = {
    station: { id: 'station', name: 'Stop', country: 'US' },
    discount: { unit: 'gal' },
    fuel: {
      miles: 100,
      visits: [{ number: 1, miles: 100, gallons: 25, purchaseCostUsd: 75 }],
    },
  };
  const all = node => [node, ...node.children.flatMap(all)];
  for (const [mode, expected] of [
    ['miles', '100 mi'],
    ['kilometers', '161 km'],
    ['both', '100 mi · 161 km'],
  ]) {
    unit = mode;
    popup.update(data);
    assert.equal(
      all(popup.element).find(
        node => node.className === 'fleet-fuel-visit__distance',
      ).textContent,
      expected,
    );
    assert.equal(
      all(popup.element).find(
        node => node.className === 'fleet-fuel-visit__buy',
      ).textContent,
      '25 US gal',
    );
  }
  unit = 'kilometers';
  popup.update({ ...data, fuel: { miles: 100 } });
  assert.ok(
    all(popup.element).some(node => node.textContent === '161 km away'),
  );
  assert.equal(data.fuel.visits[0].gallons, 25);
});

test('station editing is explicit, uses occurrence identity and disappears without a selected truck', t => {
  const previous = globalThis.document;
  const element = () => ({
    children: [],
    listeners: {},
    append(...nodes) {
      this.children.push(...nodes);
    },
    replaceChildren(...nodes) {
      this.children = nodes;
    },
    setAttribute() {},
    addEventListener(name, handler) {
      this.listeners[name] = handler;
    },
    removeEventListener(name) {
      delete this.listeners[name];
    },
    remove() {},
  });
  globalThis.document = { createElement: element, createElementNS: element };
  t.after(() => {
    globalThis.document = previous;
  });
  const calls = [];
  const popup = createStationPopup(value => calls.push(value));
  const station = { id: 'station-id', name: 'A station', address: 'A road' };
  const data = { station, discount: {}, canEdit: true };
  popup.update(data);
  const actions = popup.element.children.find(
    node => node.className === 'fleet-station-popup__actions',
  );
  assert.equal(calls.length, 0, 'opening a station never changes a plan');
  assert.equal(actions.hidden, false);
  assert.equal(actions.children[0].textContent, 'Add to fuel plan');
  actions.children[0].listeners.click({ stopPropagation() {} });
  assert.deepEqual(calls[0], {
    stationId: 'station-id',
    name: 'A station',
    beforeStopId: null,
    addNew: true,
  });
  popup.update({
    ...data,
    fuel: { visits: [{ number: 3, beforeStopId: 'leg-stop', gallons: 20 }] },
  });
  assert.equal(actions.children[0].textContent, 'Edit fuel plan');
  assert.equal(
    popup.element.className,
    'fleet-station-popup fleet-station-popup--planned fleet-station-popup--single',
  );
  assert.equal(
    popup.element.children.find(
      node => node.className === 'fleet-station-popup__plan-label',
    ).textContent,
    'Fuel stop 3',
  );
  assert.equal(actions.children[1].hidden, false);
  actions.children[0].listeners.click({ stopPropagation() {} });
  assert.equal(calls[1].beforeStopId, 'leg-stop');
  assert.equal(calls[1].addNew, false);
  actions.children[1].listeners.click({ stopPropagation() {} });
  assert.equal(calls[2].addNew, true);
  assert.equal(calls[2].beforeStopId, null);
  popup.update({ ...data, canEdit: false });
  assert.equal(actions.hidden, true);
  actions.children[0].listeners.click({ stopPropagation() {} });
  assert.equal(
    calls.length,
    3,
    'a formerly visible action must not edit after deselection',
  );
  popup.dispose();
  assert.equal(actions.children[0].listeners.click, undefined);
});

test('unchanged polling does not mutate popup content or trigger InfoWindow layout', () => {
  let writes = 0;
  globalThis.document = {
    createElement() {
      return new Proxy(
        {
          children: [],
          append(...nodes) {
            this.children.push(...nodes);
          },
          setAttribute() {},
          addEventListener() {},
          removeEventListener() {},
          remove() {},
        },
        {
          set(target, name, value) {
            writes++;
            target[name] = value;
            return true;
          },
        },
      );
    },
    createElementNS() {
      return this.createElement();
    },
  };
  const popup = createStationPopup();
  assert.equal(
    popup.element.children.filter(
      node => node.className === 'fleet-station-popup__close',
    ).length,
    0,
    'The details-card shell owns the sole close control',
  );
  const data = {
    station: { name: 'Test', address: 'Street' },
    discount: {
      retailPrice: 5,
      discountPrice: 4,
      priceAfterIfta: 3,
      savings: 1,
    },
  };
  popup.update(data);
  writes = 0;
  for (let i = 0; i < 20; i++) popup.update(data);
  assert.equal(writes, 0);
  popup.update({ ...data, discount: { ...data.discount, discountPrice: 4.1 } });
  assert.equal(writes, 1);
});

test('return visits display distinct numbers quantities and distances without polling churn', () => {
  let replacements = 0;
  globalThis.document = {
    createElement() {
      return {
        children: [],
        append(...nodes) {
          this.children.push(...nodes);
        },
        replaceChildren(...nodes) {
          replacements++;
          this.children = nodes;
        },
        setAttribute() {},
        addEventListener() {},
        removeEventListener() {},
        remove() {},
      };
    },
  };
  const popup = createStationPopup();
  const data = {
    station: { name: 'LOVES #706', address: 'Street', country: 'US' },
    discount: {},
    fuel: {
      visits: [
        {
          number: 1,
          gallons: 66,
          miles: 369,
          arrivalGallons: 44,
          departureGallons: 110,
          tankGallons: 200,
        },
        {
          number: 2,
          gallons: 164,
          full: true,
          miles: 927,
          arrivalGallons: 26,
          departureGallons: 190,
          tankGallons: 200,
        },
      ],
    },
  };
  globalThis.document.createElementNS = () =>
    globalThis.document.createElement();
  popup.update(data);
  const visits = popup.element.children.find(
    node => node.className === 'fleet-station-popup__visits',
  );
  assert.equal(visits.hidden, false);
  assert.deepEqual(
    visits.children.map(
      row => row.children[0].children[0].children[0].textContent,
    ),
    ['1', '2'],
  );
  assert.deepEqual(
    visits.children.map(
      row => row.children[0].children[0].children[1].textContent,
    ),
    ['Fuel stop', 'Fuel stop'],
  );
  assert.equal(
    popup.element.children.find(
      node => node.className === 'fleet-station-popup__plan-label',
    ).textContent,
    'Fuel stops 1, 2',
  );
  assert.equal(
    visits.children[0].children[0].children[1].textContent,
    '369 mi · 594 km',
  );
  assert.equal(
    visits.children[1].children[0].children[1].textContent,
    '927 mi · 1,492 km',
  );
  const first = visits.children[0].children[1];
  assert.equal(first.children[0].children[1].children[1].textContent, '22%');
  assert.equal(first.children[1].children[1].textContent, '66 US gal');
  assert.equal(first.children[2].children[1].children[1].textContent, '55%');
  for (let i = 0; i < 20; i++) popup.update(data);
  assert.equal(replacements, 1);
  data.fuel.visits[0].arrivalGallons = 42;
  popup.update(data);
  assert.equal(
    replacements,
    2,
    'changed arrival updates the gauge even if purchase and miles are unchanged',
  );
  popup.update({ ...data, fuel: null });
  assert.equal(visits.hidden, true);
  assert.equal(visits.children.length, 0);
});

test('station address uses separate street and locality lines while copying the original full address', async t => {
  const previousDocument = globalThis.document;
  const navigatorDescriptor = Object.getOwnPropertyDescriptor(
    globalThis,
    'navigator',
  );
  let copied;
  globalThis.document = {
    createElement() {
      return {
        children: [],
        listeners: {},
        append(...nodes) {
          this.children.push(...nodes);
        },
        setAttribute() {},
        addEventListener(name, callback) {
          this.listeners[name] = callback;
        },
        removeEventListener() {},
        remove() {},
      };
    },
  };
  Object.defineProperty(globalThis, 'navigator', {
    configurable: true,
    value: {
      clipboard: {
        async writeText(value) {
          copied = value;
        },
      },
    },
  });
  t.after(() => {
    globalThis.document = previousDocument;
    if (navigatorDescriptor)
      Object.defineProperty(globalThis, 'navigator', navigatorDescriptor);
    else delete globalThis.navigator;
  });
  const popup = createStationPopup();
  const fullAddress = '3499 Lee Jackson Hwy, Staunton, VA 24401, USA';
  const data = {
    station: { name: 'LOVES', address: fullAddress },
    discount: {},
  };
  popup.update(data);
  const address = popup.element.children.find(
    node => node.className === 'fleet-station-popup__address',
  );
  assert.equal(address.children[0].textContent, '3499 Lee Jackson Hwy');
  assert.equal(address.children[1].textContent, 'Staunton, VA 24401, USA');
  assert.equal(address.children[1].hidden, false);
  await address.listeners.click({ stopPropagation() {} });
  assert.equal(copied, fullAddress);
  popup.update({
    ...data,
    station: { ...data.station, address: 'Warehouse entrance' },
  });
  assert.equal(address.children[0].textContent, 'Warehouse entrance');
  assert.equal(address.children[1].hidden, true);
  popup.dispose();
});

test('planned purchase cards show server USD totals even at Canadian stations and hide unavailable totals', t => {
  const previous = globalThis.document;
  const element = () => ({
    children: [],
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
  globalThis.document = { createElement: element, createElementNS: element };
  t.after(() => {
    globalThis.document = previous;
  });
  const popup = createStationPopup();
  const visit = { number: 1, gallons: 60, purchaseCostUsd: 287.64 };
  const data = {
    station: { name: 'Canadian fuel', country: 'CA' },
    discount: { currency: 'CAD', unit: 'L' },
    fuel: { visits: [visit] },
  };
  popup.update(data);
  const visits = popup.element.children.find(
    node => node.className === 'fleet-station-popup__visits',
  );
  const actions = popup.element.children.find(
    node => node.className === 'fleet-station-popup__actions',
  );
  const cost = actions.children.find(
    node => node.className === 'fleet-station-popup__cost',
  );
  assert.equal(cost.children[1].textContent, '≈ $287.64 USD');
  assert.equal(
    actions.hidden,
    false,
    'the cost remains visible without edit permission',
  );
  assert.equal(actions.children[0].hidden, true);
  assert.equal(
    visits.children[0].children.length,
    2,
    'a single visit has one cost footer, not a duplicate inside the gauges',
  );
  visit.purchaseCostUsd = 289.5;
  popup.update(data);
  assert.equal(cost.children[1].textContent, '≈ $289.50 USD');
  for (const value of [null, undefined, NaN, Infinity, -1]) {
    visit.purchaseCostUsd = value;
    popup.update(data);
    assert.equal(cost.hidden, true);
    assert.equal(cost.children[1].textContent, '');
  }
  visit.purchaseCostUsd = 120;
  popup.update({
    ...data,
    fuel: { visits: [visit, { ...visit, number: 2, purchaseCostUsd: 230 }] },
  });
  assert.equal(
    cost.hidden,
    true,
    'return visits do not fabricate an aggregate client total',
  );
  assert.deepEqual(
    visits.children.map(
      row =>
        row.children.find(
          node => node.className === 'fleet-station-popup__visit-cost',
        ).children[1].textContent,
    ),
    ['≈ $120.00 USD', '≈ $230.00 USD'],
  );
  popup.dispose();
});
