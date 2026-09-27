import test from 'node:test';
import assert from 'node:assert/strict';
import {
  markCore,
  stopAppearance,
  stopMarkerIcon,
  isDelivery,
} from '../../Scripts/fleetMap/rendering/stopAppearance.ts';

// Every map mark shares one instrument core: a dark glass disc under a fine
// rim of the load's colour, the letter in that colour (the owner,
// September 27). No white discs or chunky white edges.
test('a stop still to come is a dark core with its load colour as rim and letter', () => {
  for (const color of [
    [46, 80, 231],
    [147, 98, 217, 100],
  ]) {
    const accent = [...color.slice(0, 3), 255];
    for (const job of ['Pick Up', 'Delivery', 'Drop Off', 'drop_off']) {
      assert.deepEqual(stopAppearance(job, color), {
        fill: markCore,
        border: accent,
        text: accent,
      });
    }
    assert.ok(isDelivery('DropOff'));
  }
});

test('stop backgrounds are high-density circles with a fine rim, not text boxes', () => {
  const { fill, border } = stopAppearance('Delivery', [32, 122, 99, 255]);
  const icon = stopMarkerIcon(fill, border);
  assert.equal(icon.width, 112);
  assert.equal(icon.height, 112);
  assert.equal(icon.anchorX, icon.width / 2);
  assert.equal(icon.anchorY, icon.height / 2);
  assert.equal(icon.mask, false);
  const svg = decodeURIComponent(icon.url.split(',').slice(1).join(','));
  assert.match(svg, /width="112" height="112" viewBox="0 0 28 28"/);
  assert.match(svg, /<circle cx="14" cy="14" r="12.5"/);
  assert.match(
    svg,
    /fill="rgb\(11,22,38\)" fill-opacity="0.92" stroke="rgb\(32,122,99\)" stroke-opacity="1.00" stroke-width="1.75"/,
  );
  assert.doesNotMatch(svg, /stroke-dasharray/);
  assert.doesNotMatch(svg, /<(?:rect|ellipse|text)\b/);
});

// A stop behind the truck no longer asks for anything: its rim turns quiet
// and dashed and its letter quiet, on the same core and at a smaller
// radius, so it never reads as the louder of the two.
test('a stop already visited has a quiet dashed rim', () => {
  const color = [32, 122, 99, 255];
  const done = stopAppearance('Pickup', color, true);
  assert.deepEqual(done.fill.slice(0, 3), markCore.slice(0, 3));
  assert.ok(done.border[3] < 255 && done.text[3] < 255);
  const svg = decodeURIComponent(
    stopMarkerIcon(done.fill, done.border, 11).url,
  );
  assert.match(svg, /r="11"/);
  assert.match(svg, /stroke="rgb\(32,122,99\)" stroke-opacity="0.59"/);
  assert.match(svg, /stroke-dasharray="2.2 1.6"/);
});

// Two marks on one point meant one had to be moved off the place it names,
// so neither moves any more: a truck standing on a stop is drawn as a ring
// around its badge, and a truck it has not reached yet is a disc behind a
// badge on a dark rim. The gap between them stays the distance.
test('a truck at a stop is its ring, and a truck near it stands behind', () => {
  const blue = [40, 76, 220, 255];
  const { fill, border } = stopAppearance('Delivery', blue);
  const at = decodeURIComponent(
    stopMarkerIcon(fill, border, undefined, '#16a34a').url,
  );
  assert.match(at, /viewBox="0 0 36 36"/);
  assert.match(at, /r="16.75" fill="#16a34a" stroke="rgb\(11,22,38\)"/);
  const near = decodeURIComponent(
    stopMarkerIcon(fill, border, undefined, null, true).url,
  );
  assert.match(near, /viewBox="0 0 32 32"/);
  assert.match(near, /r="15" fill="rgb\(11,22,38\)"/, 'the dark rim');
  const plain = decodeURIComponent(stopMarkerIcon(fill, border).url);
  assert.match(plain, /viewBox="0 0 28 28"/);
  assert.doesNotMatch(plain, /fill="rgb\(255,255,255\)"/);
});
