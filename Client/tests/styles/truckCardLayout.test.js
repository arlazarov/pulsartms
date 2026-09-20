import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const compile = name => compileString(`@use '${name}';`, { loadPaths }).css;
const card = compile('pages/fleet-map/compact-inspector');
const phone = compile('pages/fleet-map/mobile-inspector');
const markup = readFileSync(
  new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
  'utf8',
);

// The owner's marks on the open card: the load and its miles were said twice,
// "HOS" sat a card-width away from the clocks it names, the facts were a tall
// stack of mismatched rows beside an empty hole, and half the vehicle
// readings stood on two lines. These pin the layout that answered them.

test('the card says the load, its order and the miles once, in the head', () => {
  const head = markup.slice(
    markup.indexOf('fleet-map-inspector__hours">'),
    markup.indexOf('</header>'),
  );
  assert.match(head, /title="Copy load number"/);
  assert.match(head, /title="Copy order number"/);
  // The number and the bar under it measure the same thing - the run - so
  // one cannot contradict the other.
  assert.match(head, /Units\.DistanceValue\(RemainingMiles\)/);
  assert.match(
    head,
    /Units\.BothDistances[\s\S]*Units\.Kilometers\(RemainingMiles\)/,
  );
  // The line names a thing before it says it, this one included.
  assert.match(head, /__label">Left&#160;<\/span>/);
  assert.match(head, /fleet-map-mobile-summary__bar/);
  // The track is always drawn, so a run whose progress is not known yet
  // reserves the same height as one that is.
  assert.match(head, /RouteCovered \?\? 0/);
  // A truck that has run out of route is standing at its stop; the server
  // sends no forecast for it, which is not the same as nothing to say.
  assert.doesNotMatch(head, /@if \(RouteCovered/);
  const body = markup.slice(markup.indexOf('id="fleet-map-route-details"'));
  assert.doesNotMatch(body, /Copy load number|Copy order number/);
  assert.doesNotMatch(body, /fleet-map-route-info__load"/);
  // The body carries the total the bar is drawn against, not the remainder
  // the head already says.
  assert.match(body, /__label">Run</);
  assert.doesNotMatch(
    body,
    /DistanceValue\(routePlan is null \? null : RemainingMiles\)/,
  );
});

test('what is left sits between the load and the clocks, said and drawn', () => {
  assert.match(
    card,
    /__distance\s*\{[^}]*justify-items: center;[^}]*margin-inline: auto;/,
  );
  // One phrase: it shortens by ellipsis rather than folding a number away
  // from the unit it belongs to.
  assert.match(
    card,
    /__distance-text\s*\{[^}]*text-overflow: ellipsis;[^}]*white-space: nowrap;/,
  );
  assert.match(card, /__bar\s*\{[^}]*block-size: 3px;/);
  assert.match(card, /__bar > span\s*\{[^}]*background: var\(--ui-action\);/);
});

// 11006 had driven its whole route and was standing at the delivery waiting
// on tomorrow's window. The server sends an ETA with no stops in it, because
// there is nothing left to drive - which is not the same as nothing to say.
test('a truck that has run out of route says so instead of a dash', () => {
  const header = markup.slice(
    markup.indexOf('fleet-map-inspector__arrival'),
    markup.indexOf('</header>'),
  );
  assert.match(header, /AtNextStop[\s\S]{0,40}"At stop"/);
  const code = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor.cs', import.meta.url),
    'utf8',
  );
  // Only when the route is spent and the stop is still open.
  assert.match(code, /Tracking\.AllStopsPassed: false \} plan/);
  assert.match(code, /plan\.Tracking\.NextStopId is not null/);
  assert.match(code, /remaining < 0\.5/);
});

test('HOS travels with its clocks, at the far end under the arrival', () => {
  assert.match(
    markup,
    /fleet-map-inspector__clocks">\s*<span class="fleet-map-inspector__hours-label">HOS<\/span>\s*<Client\.Shared\.DriverStatus\.DriverHours\.DriverHours/,
  );
  assert.match(card, /__clocks\s*\{[^}]*margin-inline-start: auto;/);
  // The component pushes itself right on its own; inside the group it must
  // not, or the label is orphaned again.
  assert.match(
    card,
    /__clocks > \.driver-hours-panel\s*\{[^}]*margin-left: 0;/,
  );
});

test('the open card is two columns: the stop, then facts on one label column', () => {
  assert.match(
    card,
    /\.fleet-map-route-info\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\) minmax\(0, 1fr\);/,
  );
  // The last row takes the slack so the facts stay packed at the top.
  assert.match(
    card,
    /\.fleet-map-route-info\s*\{[^}]*grid-template-rows: auto auto 1fr;/,
  );
  assert.match(
    card,
    /__visit\s*\{[^}]*grid-column: 1;[^}]*grid-row: 1\s*\/\s*span 3;[^}]*border-inline-end: 1px solid/,
  );
  for (const [group, row] of [
    ['distances', 1],
    ['timing', 2],
  ])
    assert.match(
      card,
      new RegExp(`__${group}\\s*\\{[^}]*grid-column: 2;[^}]*grid-row: ${row};`),
    );
  // Every fact is the same two cells, sharing one label width.
  assert.match(
    card,
    /__metric,[^{}]*__arrival-fuel,[^{}]*\.stop-hours__arrival-cycle\s*\{[^}]*grid-template-columns: var\(--route-fact-label\) minmax\(0, 1fr\);/,
  );
  // The cycle is a row among rows, not a section under its own rule.
  assert.match(card, /\.stop-hours__cycle\s*\{[^}]*border: 0;/);
});

test('the stop keeps its children in its own column and fits four lines', () => {
  assert.match(
    card,
    /__next,[^{}]*__appointment\s*\{[^}]*grid-column: auto;[^}]*grid-row: auto;/,
  );
  assert.match(
    card,
    /__address-lines\s*\{[^}]*display: flex;[^}]*flex-wrap: wrap;/,
  );
  assert.match(card, /__facility\s*\{[^}]*flex-basis: 100%;/);
  assert.match(card, /__street:not\(:last-child\)::after\s*\{\s*content: ",";/);
});

test('narrow cards and phones stack the stop over the facts', () => {
  const narrow = card.slice(
    card.indexOf('@container map-truck-inspection (width < 40rem)'),
  );
  for (const css of [narrow, phone]) {
    assert.match(
      css,
      /\.fleet-map-route-info\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\);[^}]*grid-template-rows: none;/,
    );
    assert.match(
      css,
      /__visit\s*\{[^}]*border-inline-end: 0;[^}]*border-block-end: 1px solid/,
    );
  }
  // The phone no longer lays the vehicle out as a grid of towers.
  assert.doesNotMatch(phone, /fleet-map-truck-info/);
});

test('the vehicle is one line: every reading the same shape, place at the end', () => {
  assert.match(markup, /fleet-map-truck-info__kind">Vehicle</);
  assert.match(
    card,
    /__telemetry\s*\{[^}]*display: flex;[^}]*flex-wrap: wrap;/,
  );
  assert.match(card, /__telemetry\s*\{[^}]*--fuel-reading-value-column: auto;/);
  assert.match(
    card,
    /__reading,[^{}]*__outside\s*\{[^}]*display: flex;[^}]*align-items: baseline;[^}]*border: 0;/,
  );
  assert.match(card, /__location\s*\{[^}]*margin-inline-start: auto;/);
  // Only the icons the words repeat are hidden; a reading never is.
  assert.match(card, /__telemetry svg\s*\{\s*display: none;/);
  assert.doesNotMatch(card, /__reading\s*\{[^}]*display: none/);
});

// Seen on the owner's own screen at full card width: "Delivery" had dropped
// under its label, the address missed the vehicle line by a few pixels, and
// the load number was the faintest thing on a line that is about the load.
test('the lines that should be one line are one line', () => {
  assert.match(card, /__appointment\s*\{[^}]*flex-direction: row;/);
  // The page-wide rule gives the address a row of its own; the card must
  // take that back or the address never joins the vehicle line.
  assert.match(card, /__location\s*\{[^}]*flex: 0 1 auto;/);
  // "mph" and the degree sign name themselves; their words leave the line
  // but stay in the document for a screen reader.
  assert.match(
    card,
    /__reading--speed > small,[^{}]*__outside > small\s*\{[^}]*clip-path: inset\(50%\);/,
  );
  assert.match(markup, /fleet-map-truck-info__reading--speed/);
  assert.match(
    card,
    /__remaining\s*>\s*\.fleet-map-route-info__copy-number\s*\{[^}]*font-weight: 600;/,
  );
});

test('sections are separated by hairlines and actions read as buttons', () => {
  assert.match(card, /__actions\s*\{[^}]*border-top: 1px solid/);
  assert.match(card, /\.fleet-map-truck-info\s*\{[^}]*border-top: 1px solid/);
  // Labelled actions keep their outline; only the close control is bare.
  assert.doesNotMatch(
    card,
    /__actions \.map-action-icon,[^{}]*__close\s*\{[^}]*border-color: transparent/,
  );
});
