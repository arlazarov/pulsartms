import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const compile = name => compileString(`@use '${name}';`, { loadPaths }).css;
// The card, and the vehicle line that stands on it - each described in one
// place, neither overriding the other.
const card =
  compileString(
    "@use 'pages/fleet-map/inspector/card';" +
      " @use 'pages/fleet-map/inspector/hours-line';" +
      " @use 'pages/fleet-map/inspector/route-facts';" +
      " @use 'pages/fleet-map/inspector/narrow';",
    { loadPaths },
  ).css + compile('pages/fleet-map/truck-info');
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
  // The number and the bar under it measure the same thing - the way to the
  // stop the truck is heading for, which is the stop the ETA beside them is
  // for - so none of the three can contradict another. It was the remainder
  // of the whole run: a truck on its way to a pickup read the miles to its
  // delivery next to the hour of its pickup.
  assert.match(head, /Units\.DistanceValue\(LeftMiles\)/);
  assert.match(
    head,
    /Units\.BothDistances[\s\S]*Units\.Kilometers\(LeftMiles\)/,
  );
  assert.doesNotMatch(head, /DistanceValue\(RemainingMiles\)/);
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
  // The line is three columns and this is the middle one, so it is centred
  // by where it stands - it carries no margin to centre itself with. The
  // clocks column never goes under what it says: on a card with no work to
  // show it was squeezed and "Break" came apart into letters.
  assert.match(
    card,
    /__hours\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\) auto minmax\(max-content, 1fr\);/,
  );
  assert.doesNotMatch(
    card.match(/__clocks\s*\{([^}]*)\}/)[1],
    /^\s*min-width:/m,
  );
  assert.match(card, /__distance\s*\{[^}]*justify-items: center;/);
  assert.doesNotMatch(card, /__distance\s*\{[^}]*margin-inline: auto;/);
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
  assert.match(card, /__clocks\s*\{[^}]*justify-self: end;/);
  // Said once: a second flex-wrap lower in the same rule used to take back
  // the first.
  assert.equal(
    card.match(/__clocks\s*\{([^}]*)\}/)[1].match(/flex-wrap:/g).length,
    1,
  );
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
  // Every fact is the same two cells, sharing one label width - the
  // forecast's cycle row included, which reads that width from the card
  // through its compact reading rather than being restyled from here.
  assert.match(
    card,
    /__metric,[^{}]*__arrival-fuel\s*\{[^}]*grid-template-columns: var\(--route-fact-label\) minmax\(0, 1fr\);/,
  );
  assert.doesNotMatch(card, /\.stop-hours__/);
  const hours = compile('shared/driver-status/stop-hours');
  assert.match(
    hours,
    /\.stop-hours--compact \.stop-hours__arrival-cycle\s*\{[^}]*grid-template-columns: var\(--route-fact-label\) minmax\(0, 1fr\);/,
  );
  // The cycle is a row among rows, not a section under its own rule.
  assert.match(
    hours,
    /\.stop-hours--compact \.stop-hours__cycle\s*\{[^}]*border: 0;/,
  );
  assert.match(
    readFileSync(
      new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
      'utf8',
    ),
    /Reading="compact"/,
  );
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

// A card too narrow for two columns stacks - and that is decided once, by
// the card's own width. A phone used to be told the same thing a second
// time by its screen width, in a stylesheet of its own, and the two could
// disagree: a 700px window has room for both columns and stacked anyway.
test('a card too narrow for two columns stacks, wherever it stands', () => {
  const narrow = card.slice(
    card.indexOf('@container map-truck-card (width < 40rem)'),
  );
  assert.match(
    narrow,
    /\.fleet-map-route-info\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\);[^}]*grid-template-rows: none;/,
  );
  assert.match(
    narrow,
    /__visit\s*\{[^}]*border-inline-end: 0;[^}]*border-block-end: 1px solid/,
  );
  assert.match(
    narrow,
    /__appointment > strong\s*\{[^}]*overflow-wrap: normal;/,
  );
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
    /__reading,[^{}]*__outside\s*\{[^}]*display: flex;[^}]*align-items: baseline;/,
  );
  assert.match(card, /__location\s*\{[^}]*margin-inline-start: auto;/);
  // Speed, fuel and engine say themselves in words, so their icons only
  // repeat. The sky does not: the same degrees are a different day in rain
  // than in sun, so the weather keeps its icon - sun, moon, cloud, rain,
  // snow or thunder - and only the word "Temp" leaves the line.
  assert.match(card, /__reading svg\s*\{\s*display: none;/);
  assert.match(
    card,
    /__outside > small > svg\s*\{[^}]*inline-size: var\(--type-heading\);/,
  );
  // The degrees keep the baseline, which is what puts them on the level of
  // the speed and the fuel beside them; only the icon steps off it, or a
  // 20px glyph on the baseline of 14px text stands above the words.
  assert.match(card, /__outside > small\s*\{[^}]*align-self: center;/);
  // Named plainly, after the rule it differs from. It used to be named by
  // two classes at once, only to outrank a rule that stood below it.
  assert.doesNotMatch(card, /__outside\.truck-weather/);
  assert.doesNotMatch(card, /__outside > small > svg\s*\{[^}]*display: none/);
  assert.doesNotMatch(card, /__reading\s*\{[^}]*display: none/);
});

// Seen on the owner's own screen at full card width: "Delivery" had dropped
// under its label, the address missed the vehicle line by a few pixels, and
// the load number was the faintest thing on a line that is about the load.
test('the lines that should be one line are one line', () => {
  assert.match(card, /__appointment\s*\{[^}]*flex-direction: row;/);
  // The page-wide rule gives the address a row of its own; the card must
  // take that back or the address never joins the vehicle line.
  assert.match(card, /__location\s*\{[^}]*margin-inline-start: auto;/);
  // "mph" and the degree sign name themselves; their words leave the line
  // but stay in the document for a screen reader.
  assert.match(
    card,
    /__reading--speed > small,[^{}]*__outside > small > span\s*\{[^}]*clip-path: inset\(50%\);/,
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

// The card writes two rows of its own: the dash where an ETA would be, and
// the dash where the cycle would be. They stand beside rows the forecast
// renders, so they must read the same way - once the card's own stylesheet
// stopped restyling the forecast, a row that did not ask for the compact
// reading went back to the component's default and stood out in bold.
test("the card's own dashes read like the forecast they stand in for", () => {
  for (const [, placeholder] of markup.matchAll(
    /class="[^"]*(?:arrival|eta)-placeholder([^"]*)"/g,
  ))
    assert.match(placeholder, /stop-hours stop-hours--compact/);
});
