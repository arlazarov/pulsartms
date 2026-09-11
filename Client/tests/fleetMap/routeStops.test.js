import test from 'node:test';
import assert from 'node:assert/strict';
import { createRouteStops } from '../../Scripts/fleetMap/routes/routeStops.js';
import { stopEtaLabels } from '../../Scripts/fleetMap/routes/stopEtaLabels.js';

function fixture(t) {
  const originalDocument = globalThis.document;
  const calls = { opens: 0, shows: [], creates: 0, labelWrites: 0 };
  const state = { shown: null };
  const markers = [];
  globalThis.document = { createElement(tagName) {
    calls.creates++;
    return { tagName, children: [], listeners: {}, append(...children) { this.children.push(...children); },
      setAttribute(name, value) { this[name] = value; if (name === 'class') this.className = value; },
      addEventListener(name, callback) { this.listeners[name] = callback; },
      set innerHTML(value) { throw new Error(`Unexpected HTML parsing: ${value}`); } };
  } };
  globalThis.document.createElementNS = (_, tagName) => globalThis.document.createElement(tagName);
  class Marker {
    constructor(options) { this.distance = null; Object.assign(this, options); markers.push(this); }
    setNumber(number) { this.number = number; }
    setJob(job) { this.job = job; }
    setDistance(text, tones) { calls.labelWrites++; this.distance = text; this.tones = tones; }
  }
  const stops = createRouteStops({}, Marker, {
    show(content) { state.shown = content; calls.shows.push(content); },
    hide() { state.shown = null; },
  }, () => { calls.opens++; });
  t.after(() => { stops.clear(); globalThis.document = originalDocument; });
  return { stops, markers, calls, state };
}

const stop = (values = {}) => ({ id: 'pickup', job: 'Pickup', name: 'Warehouse', address: '123 Main Street',
  scheduledDate: '2026-09-09', scheduledTime: '00:00:00', point: { latitude: 40, longitude: -79 }, ...values });
const plan = (stops = [stop()]) => ({ stops, fromCurrentPosition: true, route: { legs: stops.map(() => ({ miles: 100 })) } });
const rows = content => (content.children ?? []).flatMap(child => [child, ...rows(child)]);
const row = (content, className) => rows(content).find(node => node.className?.split(' ').includes(className));
const fieldValue = (content, className) => row(content, className).children.find(node => node.tagName === 'dd').children[0].textContent;

test('arrival fuel is always visible, uses exact stop identity and clears invalidated balances', t => {
  const {stops, markers, state} = fixture(t);
  const value = {...plan(), dispatchId: 'load', fuelPlan: {needsRefresh: false, stopArrivals: [
    {dispatchId: 'other', stopId: 'pickup', gallons: 200, percent: 80},
    {dispatchId: 'load', stopId: 'pickup', gallons: 82, percent: 32.8}]}};
  stops.setPlan(value);
  markers[0].onSelect();
  assert.ok(row(row(state.shown, 'fleet-route-popup__information'), 'fleet-route-popup__fuel'));
  assert.equal(row(row(state.shown, 'fleet-route-popup__location'), 'fleet-route-popup__fuel'), undefined);
  assert.equal(row(state.shown, 'fleet-fuel-visit__percent').textContent, '33%');
  assert.equal(row(state.shown, 'fleet-fuel-visit__quantity').textContent, '82 US gal');
  assert.equal(row(state.shown, 'driver-hours__arc')['stroke-dasharray'], '33 100');
  stops.setPlan({...value, fuelPlan: {...value.fuelPlan, needsRefresh: true}});
  assert.equal(row(state.shown, 'fleet-fuel-visit__percent').textContent, '—');
  stops.setPlan({...value, fuelPlan: null});
  assert.equal(row(state.shown, 'fleet-fuel-visit__percent').textContent, '—');
  assert.equal(markers.length, 1);
});

test('current popup separates secondary cycle warning while keeping lateness with the arrival time', t => {
  const { stops, markers, state } = fixture(t);
  stops.setPlan(plan());
  for (const cycleVerified of [true, false]) {
    const label = stopEtaLabels({ validUntil: '2026-09-08T12:02:00Z', stops: [{ dispatchId: 'load', stopId: 'pickup',
      arrival: '2026-09-10T14:00:00-04:00', timeZoneId: 'America/Toronto', appointment: '2026-09-10T13:00:00-04:00',
      lateMinutes: 60, hours: { cycleVerified, cycleAtArrivalMinutes: -80, cycleAfterStopMinutes: -200, alternatives: [] }
    }] }, Date.parse('2026-09-08T12:00:00Z')).get('load:pickup');
    stops.setEtas(new Map([['pickup', label]]));
    markers[0].onSelect();
    const arrival = row(state.shown, 'fleet-route-popup__arrival');
    assert.equal(arrival.textContent, 'Sep 10 · 02:00 PM');
    assert.equal(arrival.children[0].textContent, 'Late');
    const warning = row(state.shown, 'fleet-route-popup__cycle-status');
    assert.equal(warning.textContent, cycleVerified ? 'Cycle short' : 'Cycle unknown');
    assert.ok(!arrival.children.includes(warning), 'cycle warning is not inside the inline arrival status');
    assert.ok(row(state.shown, 'fleet-route-popup__value--cycle').children.includes(warning));
  }
});

test('an unavailable current stop keeps one compact ETA field without internal maintenance diagnostics', t => {
  const { stops, markers, state } = fixture(t);
  stops.setPlan(plan());
  const reason = 'ETA unavailable: waiting for the saved preceding connection.';
  stops.setEtas(stopEtaLabels({ validUntil: '2026-09-08T12:02:00Z', stops: [],
    unavailableReason: reason, routeUpdatePending: true }, Date.parse('2026-09-08T12:00:00Z')));
  markers[0].onSelect();

  assert.equal(fieldValue(state.shown, 'fleet-route-popup__eta'), '—');
  assert.equal(rows(state.shown).filter(node => node.className?.split(' ').includes('fleet-route-popup__eta')).length, 1);
  assert.ok(rows(state.shown).every(node => ![node.textContent, node.title, node['aria-label']].includes(reason)));
  assert.equal(row(state.shown, 'stop-hours__cycle'), undefined);
});

test('both current stops show and copy authoritative load and order numbers', async t => {
  const { stops, markers, state } = fixture(t);
  const dispatchId = '33333333-3333-3333-3333-333333333333';
  const originalNavigator = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  const copied = [];
  Object.defineProperty(globalThis, 'navigator', { configurable: true,
    value: { clipboard: { writeText: async value => copied.push(value) } } });
  t.after(() => originalNavigator ? Object.defineProperty(globalThis, 'navigator', originalNavigator) : delete globalThis.navigator);
  stops.setPlan({ ...plan([stop(), stop({ id: 'delivery', job: 'Drop Off' })]), dispatchId });
  stops.setLoadReference({ dispatchId, loadNumber: 1373, loadLabel: 'AMF1373', orderNumber: '566126837' });
  for (const marker of markers) {
    marker.onSelect();
    const header = row(state.shown, 'fleet-map-route-info__load');
    assert.equal(state.shown.children[0].children[0], header, 'identity precedes the stop/company/address');
    const buttons = rows(header).filter(node => node.tagName === 'button');
    assert.deepEqual(buttons.map(button => button.children[0].textContent), ['AMF1373', '566126837']);
    assert.deepEqual(buttons.map(button => button.title), ['Copy load number', 'Copy order number']);
    for (const button of buttons) {
      let stopped = false;
      await button.listeners.click({ stopPropagation() { stopped = true; } });
      assert.ok(stopped, 'copy must not deselect the map stop');
      assert.equal(button.title, 'Copied');
    }
  }
  assert.deepEqual(copied, ['1373', '566126837', '1373', '566126837']);
});

test('presentation prefix updates only popup text and copies the original numeric identity', async t => {
  const { stops, markers, state, calls } = fixture(t);
  const dispatchId = '33333333-3333-3333-3333-333333333333';
  const originalNavigator = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  const copied = [];
  Object.defineProperty(globalThis, 'navigator', { configurable: true,
    value: { clipboard: { writeText: async value => copied.push(value) } } });
  t.after(() => originalNavigator ? Object.defineProperty(globalThis, 'navigator', originalNavigator) : delete globalThis.navigator);
  stops.setPlan({ ...plan(), dispatchId });
  stops.setLoadReference({ dispatchId, loadNumber: 1373 });
  markers[0].onSelect();
  const displayed = () => rows(row(state.shown, 'fleet-map-route-info__load')).find(node => node.tagName === 'button');
  assert.equal(displayed().children[0].textContent, '1373', 'unknown settings never invent a prefix');
  for (const loadLabel of ['AMF1373', 'ZX-1373', '1373', '<img src=x onerror=alert(1)>1373']) {
    stops.setLoadReference({ dispatchId, loadNumber: 1373, loadLabel });
    assert.equal(displayed().children[0].textContent, loadLabel);
    await displayed().listeners.click({ stopPropagation() {} });
    assert.ok(rows(state.shown).every(node => node.tagName !== 'img'), 'prefix is plain text, never HTML');
  }
  assert.deepEqual(copied, ['1373', '1373', '1373', '1373']);
  assert.equal(markers.length, 1, 'prefix updates do not rebuild stop markers');
  assert.equal(calls.opens, 1, 'prefix changes do not reselect a stop');
  const content = state.shown, creates = calls.creates;
  stops.setLoadReference({ dispatchId, loadNumber: 1373, loadLabel: '<img src=x onerror=alert(1)>1373' });
  assert.equal(state.shown, content);
  assert.equal(calls.creates, creates);
});

test('late reference updates reuse unchanged content, reject another load and never reopen closed stops', t => {
  const { stops, markers, calls, state } = fixture(t);
  const dispatchId = '33333333-3333-3333-3333-333333333333';
  const reference = { dispatchId, loadNumber: 1373, orderNumber: '566126837' };
  const route = { ...plan(), dispatchId };
  stops.setPlan(route);
  markers[0].onSelect();
  assert.equal(row(state.shown, 'fleet-map-route-info__load'), undefined);
  stops.setLoadReference(reference);
  const content = state.shown, creates = calls.creates;
  stops.setLoadReference({ ...reference });
  stops.setPlan(structuredClone(route));
  assert.equal(state.shown, content);
  assert.equal(calls.creates, creates, 'unchanged metadata must not rebuild the card');
  stops.setLoadReference({ ...reference, dispatchId: '44444444-4444-4444-4444-444444444444', loadNumber: 1376 });
  assert.equal(state.shown, content, 'late details from another dispatch cannot replace the active identity');
  stops.close();
  stops.setLoadReference({ ...reference, orderNumber: 'new-order' });
  assert.equal(state.shown, null);
  markers[0].onSelect();
  assert.ok(rows(state.shown).some(node => node.textContent === 'new-order'));
  stops.setPlan({ ...route, dispatchId: '44444444-4444-4444-4444-444444444444' });
  assert.equal(row(state.shown, 'fleet-map-route-info__load'), undefined, 'another dispatch never inherits identifiers');
  stops.setLoadReference(reference);
  assert.equal(row(state.shown, 'fleet-map-route-info__load'), undefined);
});

test('reference clipboard errors are retryable and missing order is not invented', async t => {
  const { stops, markers, state } = fixture(t);
  const dispatchId = '33333333-3333-3333-3333-333333333333';
  const originalNavigator = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  let fail = true;
  Object.defineProperty(globalThis, 'navigator', { configurable: true,
    value: { clipboard: { writeText: async () => { if (fail) throw new Error('Denied'); } } } });
  t.after(() => originalNavigator ? Object.defineProperty(globalThis, 'navigator', originalNavigator) : delete globalThis.navigator);
  stops.setPlan({ ...plan(), dispatchId });
  stops.setLoadReference({ dispatchId, loadNumber: 1373, orderNumber: '  ' });
  markers[0].onSelect();
  const header = row(state.shown, 'fleet-map-route-info__load');
  const buttons = rows(header).filter(node => node.tagName === 'button');
  assert.equal(buttons.length, 1);
  const status = rows(header).find(node => node.role === 'status');
  await buttons[0].listeners.click({ stopPropagation() {} });
  assert.match(status.textContent, /Could not copy/);
  assert.notEqual(status.className, 'visually-hidden');
  fail = false;
  await buttons[0].listeners.click({ stopPropagation() {} });
  assert.equal(status.textContent, 'Copied');
  assert.equal(status.className, 'visually-hidden');
  stops.setLoadReference(null);
  assert.equal(row(state.shown, 'fleet-map-route-info__load'), undefined);
});

test('current pickup and delivery link only to the authoritative dispatch and preserve popup caching', t => {
  const { stops, markers, calls, state } = fixture(t);
  const dispatchId = '33333333-3333-3333-3333-333333333333';
  const route = { ...plan([stop(), stop({ id: 'delivery', job: 'Drop Off' })]),
    id: '66666666-6666-6666-6666-666666666666', dispatchId };
  stops.setPlan(route);
  for (const marker of markers) {
    marker.onSelect();
    const link = row(state.shown, 'fleet-route-popup__details-link');
    assert.equal(link.tagName, 'a');
    assert.equal(link.href, `/dispatch/${dispatchId}`);
    assert.equal(link.textContent, 'Route & load details ↗');
    assert.equal(state.shown.children[1].children.at(-1), link, 'the link ends the information column');
  }
  const content = state.shown, creates = calls.creates;
  stops.setPlan(structuredClone(route));
  assert.equal(state.shown, content);
  assert.equal(calls.creates, creates);
  stops.close();
  markers[1].onSelect();
  assert.equal(state.shown, content, 'reopening retains the same link and content');
  stops.setPlan({ ...route, dispatchId: '44444444-4444-4444-4444-444444444444' });
  assert.equal(row(state.shown, 'fleet-route-popup__details-link').href, '/dispatch/44444444-4444-4444-4444-444444444444');
});

test('unknown dispatch identity hides the route link instead of guessing from the plan or stop', t => {
  const { stops, markers, state } = fixture(t);
  const route = { ...plan(), id: '66666666-6666-6666-6666-666666666666' };
  stops.setPlan(route);
  markers[0].onSelect();
  for (const dispatchId of [undefined, null, '', '00000000-0000-0000-0000-000000000000', '../other', 1376]) {
    stops.setPlan({ ...route, dispatchId });
    assert.equal(row(state.shown, 'fleet-route-popup__details-link'), undefined);
  }
});

test('current stop shows only its appointment reference under the address and updates without leaking notes', t => {
  const { stops, markers, calls, state } = fixture(t);
  const notes = 'Shipper appointment confirmation number: PU123456. Receiver appointment confirmation number: DL654321. Shipper BOL: 42845601. Service for Load sentinel.';
  const route = plan([stop({ notes })]);
  stops.setPlan(route);
  markers[0].onSelect();
  const reference = row(state.shown, 'fleet-route-popup__reference');
  assert.deepEqual(reference.children.map(child => child.textContent), ['Appt #', 'PU123456']);
  assert.ok(reference.className.split(' ').includes('fleet-route-popup__section-start'));
  assert.ok(row(state.shown, 'fleet-route-popup__distance').className.split(' ').includes('fleet-route-popup__section-start'));
  assert.equal(state.shown.children[0].children.at(-1), reference);
  assert.ok(rows(state.shown).every(child => !/DL654321|42845601|Service for Load|confirmation number/.test(child.textContent ?? '')));
  const original = state.shown, creates = calls.creates;
  stops.setPlan(plan([stop({ notes: `${notes} Other unrelated information.` })]));
  assert.equal(state.shown, original);
  assert.equal(calls.creates, creates, 'unrelated note edits must not regenerate the popup');
  stops.setPlan(plan([stop({ notes, job: 'Drop Off' })]));
  assert.deepEqual(row(state.shown, 'fleet-route-popup__reference').children.map(child => child.textContent), ['Appt #', 'DL654321']);
  assert.ok(rows(state.shown).every(child => !/PU123456|42845601|Service for Load/.test(child.textContent ?? '')));
  stops.setPlan(plan([stop({ notes: 'Shipper BOL: 42845601. Service for Load sentinel.', job: 'Drop Off' })]));
  assert.equal(row(state.shown, 'fleet-route-popup__reference'), undefined);
  assert.equal(calls.opens, 1, 'reference changes update the open card without reselecting it');
});

test('current stops keep numbered pickup and delivery circles without persistent text labels', t => {
  const { stops, markers, calls, state } = fixture(t);
  const route = plan([stop(), stop({ id: 'delivery', job: 'Delivery', name: 'Customer' })]);
  stops.setPlan(route);
  stops.setProgress(20);
  stops.setEtas(new Map([
    ['pickup', { text: 'ETA Sep 9, 03:45 AM local', tone: 'eta' }],
    ['delivery', { text: 'ETA Sep 10, 02:00 PM local', tone: 'success' }],
  ]));

  assert.deepEqual(markers.map(marker => [marker.number, marker.job, marker.distance]), [
    ['1', 'Pickup', null], ['2', 'Delivery', null],
  ]);
  assert.ok(markers.every(marker => marker.map && typeof marker.onSelect === 'function'));
  assert.equal(calls.creates, 0, 'unselected current stops do not create popup content');

  markers[1].onSelect();
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__eta'), 'ETA Sep 10, 02:00 PM local');
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__distance'), '180 mi · 290 km');
  stops.setProgress(21);
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__distance'), '179 mi · 288 km');
  stops.close();
  stops.setPlan(structuredClone(route));
  stops.setEtas(new Map());
  assert.ok(markers.every(marker => marker.distance === null));
  assert.equal(calls.labelWrites, 0, 'polling must not generate or update hidden stop labels');
});

test('current-stop details show the local appointment window, exact ETA status and remaining miles and kilometers', t => {
  const { stops, markers, calls, state } = fixture(t);
  const eta = { text: 'ETA Sep 9, 03:45 AM local · Late', arrivalText: 'Sep 9, 03:45 AM', statusText: 'Late', tone: 'danger' };
  stops.setPlan(plan([stop({ scheduledDate2: '2026-09-10', scheduledTime2: '05:15:59', commodity: 'STEELCOILS', notes: 'Service for Load' })]));
  stops.setProgress(20);
  stops.setEtas(new Map([['pickup', eta]]));
  assert.equal(calls.creates, 0, 'unselected stops do not create details content');

  markers[0].onSelect();

  assert.equal(calls.opens, 1);
  assert.equal(state.shown.className, 'fleet-route-popup fleet-route-popup--stop');
  assert.deepEqual(state.shown.children.map(section => section.className), [
    'fleet-route-popup__location', 'fleet-route-popup__information'
  ]);
  const [location, information] = state.shown.children;
  assert.deepEqual(location.children.slice(0, 2).map(node => node.textContent), ['Pickup', 'Warehouse']);
  assert.equal(location.children[2].className, 'fleet-route-popup__address');
  assert.deepEqual(location.children[2].children.map(node => node.textContent), ['123 Main Street']);
  assert.equal(location.children.length, 3, 'only the stop identity and address belong to the destination column');
  assert.deepEqual(rows(information).filter(node => node.tagName === 'dt').map(node => node.textContent),
    ['Appointment', 'ETA', 'Total']);
  assert.deepEqual(rows(state.shown).filter(node => node.tagName === 'dt').map(node => node.textContent),
    ['Appointment', 'ETA', 'Total']);
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__appointment'), 'Sep 9 · 12:00 AM – Sep 10 · 05:15 AM');
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__eta'), eta.arrivalText);
  assert.equal(row(state.shown, 'fleet-route-popup__status').textContent, 'Late');
  assert.ok(row(state.shown, 'fleet-route-popup__eta').className.split(' ').includes('fleet-route-popup__eta--danger'));
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__distance'), '80 mi · 129 km');
  assert.equal(row(state.shown, 'fleet-route-popup__timezone'), undefined);
  assert.ok(rows(state.shown).every(node => !/STEELCOILS|Commodity|Service for Load/.test(node.textContent ?? '')));
});

test('current-stop popup keeps previous content unchanged through pending grace and replaces it when ready', t => {
  const { stops, markers, calls, state } = fixture(t);
  const now = Date.parse('2026-09-08T12:00:00Z');
  const etaStop = { dispatchId: 'load', stopId: 'pickup', arrival: '2026-09-10T14:00:00-04:00',
    timeZoneId: 'America/Toronto', appointment: '2026-09-10T11:00:00-04:00', lateMinutes: 185 };
  const pending = { validUntil: '2026-09-08T12:00:00Z', routeUpdatePending: true, stops: [etaStop] };
  const setEta = (eta, time = now) => {
    const label = stopEtaLabels(eta, time).get('load:pickup');
    stops.setEtas(new Map(label ? [['pickup', label]] : []));
  };
  stops.setPlan(plan());
  stops.setProgress(20);
  setEta({ ...pending, validUntil: '2026-09-08T12:02:00Z', routeUpdatePending: false });
  markers[0].onSelect();
  const completeContent = state.shown, completeCreates = calls.creates;
  setEta(pending);
  assert.equal(state.shown, completeContent);
  assert.equal(calls.creates, completeCreates, 'entering pending does not regenerate unchanged content');
  assert.equal(row(state.shown, 'fleet-route-popup__status').textContent, 'Late');
  assert.ok(!rows(state.shown).some(node => /Updating|Previous/.test(node.textContent)));
  assert.ok(row(state.shown, 'fleet-route-popup__eta--danger'));
  assert.equal(row(state.shown, 'fleet-route-popup__eta--success'), undefined);
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__eta'), 'Sep 10, 02:00 PM');
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__distance'), '80 mi · 129 km');

  const content = state.shown, creates = calls.creates;
  setEta(pending, now + 15 * 60_000 - 1);
  assert.equal(state.shown, content);
  assert.equal(calls.creates, creates, 'unchanged pending display must not regenerate the card');
  setEta(pending, now + 15 * 60_000);
  assert.equal(row(state.shown, 'fleet-route-popup__status--previous'), undefined);
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__eta'), '—');
  setEta(pending);
  setEta({ ...pending, validUntil: '2026-09-08T12:02:00Z', routeUpdatePending: false,
    stops: [{ ...etaStop, arrival: '2026-09-10T10:00:00-04:00', lateMinutes: 0 }] });
  assert.equal(row(state.shown, 'fleet-route-popup__status--previous'), undefined);
  assert.equal(row(state.shown, 'fleet-route-popup__status').textContent, 'On time');
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__eta'), 'Sep 10, 10:00 AM');
  assert.equal(calls.opens, 1, 'refresh updates the selected card without another selection');
});

test('selected details refresh only changed display values and never coordinate another card during refresh', t => {
  const { stops, markers, calls, state } = fixture(t);
  const original = plan([stop({ notes: 'Gate B' })]);
  const eta = { text: 'ETA Sep 9, 03:45 AM local', tone: 'eta' };
  stops.setPlan(original);
  stops.setProgress(20);
  stops.setEtas(new Map([['pickup', eta]]));
  markers[0].onSelect();
  const content = state.shown;
  const creates = calls.creates;

  stops.setProgress(20.001);
  stops.setEtas(new Map([['pickup', { ...eta }]]));
  const unchanged = structuredClone(original);
  Object.assign(unchanged.stops[0], { scheduledTime: '00:00:59', notes: ' Gate B ' });
  stops.setPlan(unchanged);
  assert.equal(state.shown, content);
  assert.equal(calls.creates, creates);
  assert.equal(calls.shows.length, 1, 'unchanged display text does not replace or show content');

  stops.setProgress(21);
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__distance'), '79 mi · 127 km');
  assert.equal(calls.shows.length, 2);
  stops.setEtas(new Map([['pickup', { ...eta, tone: 'success' }]]));
  assert.ok(row(state.shown, 'fleet-route-popup__eta').className.split(' ').includes('fleet-route-popup__eta--success'));
  assert.equal(calls.shows.length, 3, 'a changed tone refreshes even when the ETA text is identical');
  stops.setEtas(new Map([['pickup', { ...eta, tone: 'success', statusText: 'On time' }]]));
  assert.equal(row(state.shown, 'fleet-route-popup__status').textContent, 'On time');
  assert.equal(calls.shows.length, 4, 'a changed status refreshes independently from arrival text and tone');
  const updated = structuredClone(unchanged);
  Object.assign(updated.stops[0], { scheduledTime: '04:30:00', address: '456 Main Street' });
  stops.setPlan(updated);
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__appointment'), 'Sep 9 · 04:30 AM');
  assert.ok(rows(state.shown).some(node => node.textContent === '456 Main Street'));
  assert.equal(calls.shows.length, 5);
  assert.equal(calls.opens, 1, 'only explicit stop selection coordinates other cards');

  const retained = state.shown;
  const retainedCreates = calls.creates;
  stops.close();
  markers[0].onSelect();
  assert.equal(state.shown, retained, 'reopening unchanged details reuses the cached content');
  assert.equal(calls.creates, retainedCreates);
});

test('ETA expiry and unknown distance stay explicit, and a closed or passed stop never reopens on updates', t => {
  const { stops, markers, calls, state } = fixture(t);
  const route = plan([stop({ scheduledDate: null, scheduledTime: null })]);
  stops.setPlan(route);
  stops.setProgress(20);
  stops.setEtas(new Map([['pickup', { text: 'ETA Sep 9, 03:45 AM local', tone: 'success' }]]));
  markers[0].onSelect();
  stops.setEtas(new Map());
  stops.setProgress(null);
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__appointment'), '—');
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__eta'), '—');
  assert.equal(row(state.shown, 'fleet-route-popup__eta').className, 'fleet-route-popup__field fleet-route-popup__eta');
  assert.equal(row(state.shown, 'fleet-route-popup__status'), undefined);
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__distance'), '—');
  const count = calls.shows.length;
  stops.setProgress(Number.NaN);
  assert.equal(calls.shows.length, count, 'different unknown progress values have the same display');
  stops.setProgress(110);
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__distance'), '0 mi · 0 km');

  stops.close();
  const closedCount = calls.shows.length;
  stops.setProgress(30);
  stops.setEtas(new Map([['pickup', { text: 'ETA Sep 9, 04:00 AM local', tone: 'eta' }]]));
  stops.setPlan(route);
  assert.equal(state.shown, null);
  assert.equal(calls.shows.length, closedCount);
  markers[0].onSelect();
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__eta'), 'ETA Sep 9, 04:00 AM local');
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__distance'), '70 mi · 113 km');

  stops.setPlan({ ...route, tracking: { passedStopIds: ['pickup'] } });
  const passedCount = calls.shows.length;
  stops.setProgress(40);
  stops.setEtas(new Map());
  assert.equal(state.shown, null);
  assert.equal(calls.shows.length, passedCount);
});

test('reference-stop details keep unknown distance and render provider text as text', t => {
  const { stops, markers, calls, state } = fixture(t);
  const name = '<img src=x onerror=alert(1)>', address = '<script>alert(1)</script>';
  const reference = stop({ id: 'reference', name, address, scheduledDate: '2026-09-09', scheduledTime: '00:00:00' });
  stops.setPlan({ ...plan(), referenceStops: [reference] });
  stops.setProgress(20);
  markers[0].onSelect();
  assert.deepEqual(state.shown.children[0].children.slice(0, 2).map(node => node.textContent), ['Pickup', name]);
  assert.deepEqual(row(state.shown, 'fleet-route-popup__address').children.map(node => node.textContent), [address]);
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__appointment'), 'Sep 9 · 12:00 AM');
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__distance'), '—');
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__eta'), '—');
  assert.ok(rows(state.shown).every(node => ['div', 'strong', 'dl', 'dt', 'dd', 'span'].includes(node.tagName)));
  markers[1].onSelect();
  assert.equal(calls.opens, 2);
  assert.equal(fieldValue(state.shown, 'fleet-route-popup__distance'), '80 mi · 129 km');
});

test('current popup renders signed hours and explicit alternatives with metadata-only updates', t => {
  const { stops, markers, calls, state } = fixture(t);
  const now = Date.parse('2026-09-08T12:00:00Z');
  const eta = { validUntil: '2026-09-08T12:02:00Z', stops: [{ dispatchId: 'load', stopId: 'pickup',
    arrival: '2026-09-10T12:00:00-04:00', timeZoneId: 'America/Toronto', appointment: '2026-09-10T18:00:00-04:00',
    lateMinutes: 0, hours: { cycleVerified: true, cycleAtArrivalMinutes: -480, cycleAfterStopMinutes: -600,
      firstCycleShortageAt: '2026-09-10T04:00:00-04:00', drivingShortfallMinutes: 480, alternatives: [
        { kind: 'recap', arrival: '2026-09-10T17:00:00-04:00', lateMinutes: 0 }
      ] } }] };
  const update = value => stops.setEtas(new Map([['pickup', stopEtaLabels(value, now).get('load:pickup')]]));
  stops.setPlan(plan());
  update(eta);
  assert.equal(calls.creates, 0);
  markers[0].onSelect();
  assert.ok(rows(state.shown).some(node => node.textContent === 'ETA'));
  assert.ok(rows(state.shown).every(node => node.textContent !== 'Road ETA'));
  assert.ok(rows(state.shown).some(node => node.textContent === 'Cycle short'));
  assert.ok(rows(state.shown).some(node => node.textContent === '−8h 00m'));
  assert.ok(rows(state.shown).some(node => node.textContent === 'On time with recap'));
  const content = state.shown, creates = calls.creates;
  update(structuredClone(eta));
  assert.equal(state.shown, content);
  assert.equal(calls.creates, creates);
  update({ ...eta, routeUpdatePending: true });
  assert.equal(state.shown, content);
  assert.equal(calls.creates, creates, 'pending does not rebuild the same Hours card');
  assert.ok(rows(state.shown).some(node => node.textContent === 'Cycle short'));
  assert.ok(rows(state.shown).some(node => node.textContent === '−8h 00m'));
  assert.ok(rows(state.shown).some(node => node.className?.includes('stop-hours__value--danger')));
  assert.equal(markers.length, 1);
  assert.equal(calls.opens, 1);
});
