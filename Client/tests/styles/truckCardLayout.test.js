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
  ).css +
  compile('pages/fleet-map/truck-info') +
  compile('shared/trucks/readings');
const markup = readFileSync(
  new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
  'utf8',
);
// The vehicle line is the shared TruckReadings, which Dispatch uses too.
const readings = readFileSync(
  new URL(
    '../../Shared/Trucks/TruckReadings/TruckReadings.razor',
    import.meta.url,
  ),
  'utf8',
);

// The owner's marks on the open card: the load and its miles were said twice,
// "HOS" sat a card-width away from the clocks it names, the facts were a tall
// stack of mismatched rows beside an empty hole, and half the vehicle
// readings stood on two lines. These pin the layout that answered them.

test('the card says the load, its order and the miles once, in the head', () => {
  const head = markup.slice(
    markup.indexOf('fleet-map-inspector__controls'),
    markup.indexOf('</header>'),
  );
  // The load is a way to its page; the order is the number to copy.
  assert.match(
    head,
    /class="fleet-map-inspector__load-link"\s*href="@OpenLoadHref"/,
  );
  assert.match(head, /title="Copy order number"/);
  // The number measures the way to the stop the truck is heading for,
  // which is the stop the ETA beside it is for. It was the remainder of
  // the whole run: a truck on its way to a pickup read the miles to its
  // delivery next to the hour of its pickup.
  assert.match(head, /Units\.DistanceValue\(LeftMiles\)/);
  assert.match(
    head,
    /Units\.BothDistances[\s\S]*Units\.Kilometers\(LeftMiles\)/,
  );
  assert.doesNotMatch(head, /DistanceValue\(RemainingMiles\)/);
  // The line names a thing before it says it, this one included.
  assert.match(head, /__label">Left&#160;<\/span>/);
  // The owner's card of September 26 says Left as a phrase, without the
  // progress bar the September 25 card drew under it.
  assert.doesNotMatch(head, /fleet-map-mobile-summary__bar|RouteCovered/);
  const body = markup.slice(markup.indexOf('id="fleet-map-route-details"'));
  assert.doesNotMatch(body, /Copy load number|Copy order number/);
  assert.doesNotMatch(body, /fleet-map-route-info__load"/);
  // The head says what is left to the next stop; the body what is left of
  // the whole load (the owner's approved card, September 25).
  assert.match(body, /__label">Remaining load</);
  assert.match(
    body,
    /DistanceValue\(routePlan is null \? null : RemainingMiles\)/,
  );
  assert.doesNotMatch(body, /DistanceValue\(LeftMiles\)/);
});

test('what is left stands beside the name', () => {
  // Where the ETA used to stand: at the right of the truck's own row. The
  // clocks never go under what they say: on a card with no work to show
  // they were squeezed and "Break" came apart into letters.
  assert.match(
    card,
    /\.fleet-map-mobile-summary__distance\s*\{[^}]*grid-area: distance;[^}]*justify-self: end;/,
  );
  assert.doesNotMatch(
    card.match(/__clocks\s*\{([^}]*)\}/)[1],
    /^\s*min-width:/m,
  );
  assert.doesNotMatch(card, /__distance\s*\{[^}]*margin-inline: auto;/);
  // One phrase: it shortens by ellipsis rather than folding a number away
  // from the unit it belongs to.
  assert.match(
    card,
    /__distance-text\s*\{[^}]*text-overflow: ellipsis;[^}]*white-space: nowrap;/,
  );
  assert.doesNotMatch(card, /__bar\b/);
});

// The owner's order for the closed card (AMF1414, truck 11007): the load
// with its ETA at the right and the booking at that same stop under it;
// the vehicle with its clocks at the right; and the address last, across
// the card, where a long one folds instead of being squeezed beside the
// readings.
test('the booking sits under the ETA for the same stop, and the address is last', () => {
  const head = markup.slice(
    markup.indexOf('fleet-map-inspector__controls'),
    markup.indexOf('</header>'),
  );
  const order = [
    'fleet-map-mobile-summary__distance',
    'fleet-map-mobile-summary__remaining',
    'fleet-map-inspector__arrival',
    'fleet-map-inspector__appointment',
    'fleet-map-inspector__vehicle',
    'fleet-map-inspector__clocks',
    'fleet-map-inspector__duty',
  ].map(name => head.indexOf(name));
  assert.ok(
    order.every(at => at >= 0),
    String(order),
  );
  assert.deepEqual(
    order,
    [...order].sort((a, b) => a - b),
  );
  // Labelled as the booking, never as the forecast, written in the stop's
  // zone, and shown only for the stop the ETA is for.
  assert.match(
    head,
    /appointment-label">Appointment<\/span>\s*<strong>\s*@foreach \(var line in HeadAppointmentLines\(HeadAppointmentStop\)\)/,
  );
  // The card's one appointment row: shown as a dash while the stop loads
  // or has no booking, and not at all while the ETA is for another stop.
  assert.match(head, /@if \(HeadAppointmentShown\)/);
  const code = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.LoadDetails.cs', import.meta.url),
    'utf8',
  );
  assert.match(code, /forecast\.StopId != stop\.Id/);
  // Where the truck is: only on the open card since September 26, beside
  // the next stop - where it is and where it is going, in one column -
  // and not among the facts on the right.
  assert.equal(head.indexOf('TruckLocationLine'), -1);
  const body = markup.slice(markup.indexOf('id="fleet-map-route-details"'));
  // The cycle and the fuel on arrival left the card; the actions stand
  // at the foot of the facts column, beside the stop.
  assert.equal(body.indexOf('fleet-map-route-info__arrival-fuel'), -1);
  // The route's own arrival stays only while a next stop has the head.
  assert.match(
    body,
    /@if \(_inspectorMode != MapInspectorMode\.Truck\)\s*\{\s*<div class="fleet-map-route-info__timing">/,
  );
  assert.ok(
    body.indexOf('fleet-map-route-info__next') <
      body.indexOf('<TruckLocationLine') &&
      body.indexOf('<TruckLocationLine') <
        body.indexOf('fleet-map-route-info__facts') &&
      body.indexOf('fleet-map-route-info__facts') <
        body.indexOf('@truckActions'),
  );
  assert.match(
    card,
    /\.fleet-map-inspector__location\s*\{[^}]*grid-template-columns: max-content minmax\(0, 1fr\);/,
  );
  assert.match(
    card,
    /\.fleet-map-inspector__location > strong > button\s*\{[^}]*overflow-wrap: anywhere;/,
  );
});

// 11006 had driven its whole route and was standing at the delivery waiting
// on tomorrow's window. The server sends an ETA with no stops in it, because
// there is nothing left to drive - which is not the same as nothing to say.
test('a truck that has run out of route says so instead of a dash', () => {
  const header = markup.slice(
    markup.indexOf('fleet-map-inspector__arrival'),
    markup.indexOf('</header>'),
  );
  assert.match(header, /AtNextStop[\s\S]{0,80}"At stop"/);
  const code = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor.cs', import.meta.url),
    'utf8',
  );
  // Only when the route is spent and the stop is still open.
  assert.match(code, /Tracking\.AllStopsPassed: false \} plan/);
  assert.match(code, /plan\.Tracking\.NextStopId is not null/);
  assert.match(code, /remaining < 0\.5/);
});

// The owner's mark on the released card: "Cycle short" fell to a line of
// its own, the booking's value did not start where the ETA's did, and the
// words, icons and values of each row stood at different heights.
test('the head reads as rows of one table, on one line each', () => {
  const hours = compile('shared/driver-status/stop-hours');
  // The ETA's label column is the holder's width at least, and the word
  // about the cycle stands beside the hour.
  assert.match(
    hours,
    /\.stop-hours--compact\s*\{[^}]*grid-template-columns: var\(--stop-hours-columns, minmax\(var\(--route-fact-label, 0px\), max-content\) minmax\(0, 1fr\)\);/,
  );
  assert.match(
    hours,
    /\.stop-hours--compact \.stop-hours__cycle-status\s*\{[^}]*flex-basis: auto;/,
  );
  assert.doesNotMatch(
    hours.match(/\.stop-hours--compact\s*\{[^}]*\}/)[0],
    /--stop-hours-road-display/,
  );
  // The booking uses the same column, as a length the row can measure.
  assert.match(
    card,
    /\.fleet-map-inspector__arrival\s*\{[^}]*--route-fact-label: 6rem;/,
  );
  // Without a booking the column is the label's own width, so "ETA" and
  // "At stop" do not run apart (the owner, September 27).
  assert.match(
    card,
    /@container map-truck-card\s+\(width >= 20rem\)\s*\{\s*[^{]*\.fleet-map-inspector__arrival:not\(:has\(> \.fleet-map-inspector__appointment\)\)\s*\{\s*--route-fact-label: max-content;/,
  );
  assert.match(
    card,
    /\.fleet-map-inspector__appointment\s*\{[^}]*grid-template-columns: minmax\(var\(--route-fact-label\), max-content\) minmax\(0, 1fr\);/,
  );
  // Too narrow for the column: each label its own width, the booking's
  // value under its label.
  assert.match(
    card,
    /@container map-truck-card\s+\(width < 20rem\)\s*\{[^@]*__arrival\s*\{\s*--route-fact-label: 0px;[^@]*__appointment\s*\{\s*grid-template-columns: minmax\(0, 1fr\);/,
  );
  // Every label is its value's size, only quieter.
  for (const rule of [
    /\.fleet-map-inspector__appointment-label\s*\{[^}]*\}/,
    /\.fleet-map-mobile-summary__label\s*\{[^}]*\}/,
  ])
    assert.doesNotMatch(card.match(rule)[0], /font-size/);
  assert.match(
    card,
    /\.truck-readings__reading > small\s*\{[^}]*font-size: inherit;/,
  );
  assert.match(card, /--hos-label-font-size: var\(--type-body\);/);
  // An icon whose word is only for a screen reader has no text to sit on:
  // it centres on the line.
  assert.match(
    card,
    /__reading--speed > small,[^{}]*__reading--outside > small\s*\{[^}]*align-self: center;/,
  );
  assert.match(
    card,
    /\.fleet-map-inspector__clocks\s*\{[^}]*align-items: center;/,
  );
  // On the open card the location is a fact among the facts: the pin
  // centred on its label, the address under it.
  assert.match(
    card,
    /\.fleet-map-inspector__location\s*\{[^}]*align-items: center;/,
  );
});

// The speed's icon says how it stands against the limit, with the shared
// telemetry tokens (TelemetryTone.Speed decides the band). The rules were
// dropped on September 20 while the icons were hidden and every band read
// grey once they came back.
test('the speed icon carries its band, and an unknown speed stays quiet', () => {
  for (const [band, token] of [
    ['is-normal', '--ui-success-text'],
    ['is-low', '--ui-telemetry-warning-icon'],
    ['is-critical', '--ui-telemetry-critical-icon'],
  ])
    assert.match(
      card,
      new RegExp(
        `__reading--speed\\.${band} > small svg\\s*\\{[^}]*color: var\\(${token}\\);`,
      ),
    );
  assert.doesNotMatch(card, /__reading--speed\.is-unknown/);
  // An engine that is off keeps the speed quiet (the owner, September 27).
  assert.doesNotMatch(card, /__reading--speed\.is-stopped/);
  assert.match(markup, /<TruckReadings Speed="KnownSpeed\(truck\)"/);
  assert.match(readings, /@TelemetryTone\.Speed\(Speed, Engine\)/);
});

test('HOS travels with its clocks, at the far end under the arrival', () => {
  assert.match(
    markup,
    /fleet-map-inspector__clocks" aria-label="HOS">\s*<DriverHours/,
  );
  // No visible "HOS" word or clock icon (the owner's September 26 card):
  // the labels say what the numbers are; "HOS" stays the group's name.
  assert.doesNotMatch(markup, /hours-label/);
  // The clocks fill the answer column from the shared hairline, spread
  // between its ends: at the far end alone, the hairline before them
  // stepped sideways from the row above.
  assert.match(card, /__clocks\s*\{[^}]*justify-content: space-between;/);
  assert.match(
    card,
    /__row\s*\{[^}]*grid-template-columns: var\(--truck-card-columns\);/,
  );
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
    /\.fleet-map-route-info\s*\{[^}]*grid-template-columns: var\(--truck-card-columns\);/,
  );
  // The stop at the left; at the right one column of facts - remaining
  // load, cycle, fuel on arrival - then where the truck is.
  assert.match(
    card,
    /__visit\s*\{[^}]*grid-column: 1;[^}]*grid-row: 1;[^}]*border-inline-end: 1px solid/,
  );
  assert.match(card, /__facts\s*\{[^}]*grid-column: 2;[^}]*grid-row: 1;/);
  // What is left of the load stands over where the truck is, in the
  // stop's second column: a label with its value under it (the owner,
  // September 26). The forecast's cycle row reads its label width from
  // the card through its compact reading rather than being restyled here.
  assert.match(
    card,
    /__where\s*\{[^}]*display: grid;[^}]*align-content: start;/,
  );
  // The stop's own rows stay together at the top beside a taller column.
  assert.match(
    card,
    /__next\s*\{[^}]*display: grid;[^}]*align-content: start;/,
  );
  assert.match(
    card,
    /__metric\s*\{[^}]*display: grid;[^}]*grid-template-columns: max-content minmax\(0, 1fr\);[^}]*align-content: start;/,
  );
  assert.match(
    card,
    /__metric > \.fleet-map-route-info__progress\s*\{[^}]*grid-column: 2;[^}]*flex-wrap: nowrap;[^}]*white-space: nowrap;/,
  );
  assert.doesNotMatch(card, /\.stop-hours__/);
  const hours = compile('shared/driver-status/stop-hours');
  // The same label width as every other fact, and a reading that keeps
  // the room it needs: with a floor of zero the label took its whole
  // width first and a phone card at 200% text cut the cycle short.
  assert.match(
    hours,
    /\.stop-hours--compact \.stop-hours__arrival-cycle\s*\{[^}]*grid-template-columns: var\(--route-fact-label\) minmax\(min-content, 1fr\);/,
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
  // The street and the city each on a line, as the approved card has them.
  assert.match(
    card,
    /__address-lines > \.fleet-map-route-info__street,[^{}]*__address\s*\{\s*flex-basis: 100%;/,
  );
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

test('the vehicle is one line: every reading the same shape', () => {
  // Each reading leads with its icon; no word stands over the group.
  assert.doesNotMatch(markup, /fleet-map-truck-info__kind/);
  assert.match(
    card,
    /__reading svg\s*\{[^}]*inline-size: var\(--type-heading\);/,
  );
  assert.match(
    card,
    /\.truck-readings\s*\{[^}]*display: grid;[^}]*grid-template-columns: repeat\(auto-fit, minmax\(min\(100%, var\(--truck-readings-min-width, var\(--size-telemetry-reading\)\)\), 1fr\)\);/,
  );
  assert.match(
    card,
    /\.truck-readings\s*\{[^}]*--fuel-reading-value-column: auto;/,
  );
  assert.match(
    card,
    /\.truck-readings__reading\s*\{[^}]*display: flex;[^}]*align-items: baseline;/,
  );
  // Where the truck is has a row of its own now, not the end of this one.
  assert.doesNotMatch(markup, /fleet-map-truck-info__location/);
  assert.doesNotMatch(card, /__location\s*\{[^}]*margin-inline-start: auto;/);
  // Every reading leads with its icon (the owner's approved card); the
  // weather's is the reading itself - sun, moon, cloud, rain, snow or
  // thunder - and only the word "Temp" leaves the line.
  assert.doesNotMatch(card, /__reading svg\s*\{\s*display: none;/);
  assert.match(
    card,
    /__reading--outside > small > svg\s*\{[^}]*inline-size: var\(--type-heading\);/,
  );
  // The degrees keep the baseline, which is what puts them on the level of
  // the speed and the fuel beside them; only the icon steps off it, or a
  // 20px glyph on the baseline of 14px text stands above the words.
  assert.match(card, /__reading--outside > small\s*\{[^}]*align-self: center;/);
  // Named plainly, after the rule it differs from. It used to be named by
  // two classes at once, only to outrank a rule that stood below it.
  assert.doesNotMatch(card, /__reading--outside\.truck-weather/);
  assert.doesNotMatch(
    card,
    /__reading--outside > small > svg\s*\{[^}]*display: none/,
  );
  assert.doesNotMatch(card, /__reading\s*\{[^}]*display: none/);
});

// Seen on the owner's own screen at full card width: "Delivery" had dropped
// under its label, the address missed the vehicle line by a few pixels, and
// the load number was the faintest thing on a line that is about the load.
test('the lines that should be one line are one line', () => {
  assert.match(card, /__appointment\s*\{[^}]*flex-direction: row;/);
  // "mph" and the degree sign name themselves; their words leave the line
  // but stay in the document for a screen reader.
  assert.match(
    card,
    /__reading--speed > small > span,[^{}]*__reading--outside > small > span\s*\{[^}]*clip-path: inset\(50%\);/,
  );
  assert.match(readings, /truck-readings__reading--speed/);
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
