import test from 'node:test';
import assert from 'node:assert/strict';
import {
  stopAppearance,
  stopMarkerIcon,
  isDelivery,
} from '../../Scripts/fleetMap/rendering/stopAppearance.js';

test('number badges use opaque route colors with a white border and readable digits', () => {
  for (const color of [
    [46, 80, 231],
    [147, 98, 217, 100],
  ]) {
    const accent = [...color.slice(0, 3), 255];
    const white = [255, 255, 255, 255];
    assert.deepEqual(stopAppearance('Pick Up', color), {
      fill: accent,
      border: white,
      text: white,
    });
    for (const job of ['Delivery', 'Drop Off', 'drop_off', 'DropOff']) {
      assert.ok(isDelivery(job));
      assert.deepEqual(stopAppearance(job, color), {
        fill: accent,
        border: white,
        text: white,
      });
    }
  }
});

test('stop backgrounds are high-density circles with centered square bounds, not rounded text boxes', () => {
  const icon = stopMarkerIcon([32, 122, 99, 255]);
  assert.equal(icon.width, 136);
  assert.equal(icon.height, 136);
  assert.equal(icon.anchorX, icon.width / 2);
  assert.equal(icon.anchorY, icon.height / 2);
  assert.equal(
    icon.mask,
    false,
    'the white border and route fill remain distinct',
  );
  const svg = decodeURIComponent(icon.url.split(',').slice(1).join(','));
  assert.match(svg, /width="136" height="136" viewBox="0 0 34 34"/);
  assert.match(svg, /<circle cx="17" cy="17" r="15.5"/);
  assert.match(
    svg,
    /fill="rgb\(32,122,99\)" stroke="rgb\(255,255,255\)" stroke-width="2.5"/,
  );
  assert.doesNotMatch(svg, /<(?:rect|ellipse|text)\b/);
});

// A stop behind the truck is outlined, not filled: it no longer asks for
// anything. The card has said it that way all along, while the map drew it
// with the same filled circle as the stop still to come.
test('a stop already visited is drawn outlined, like its badge in the card', () => {
  const color = [32, 122, 99, 255];
  const pending = stopAppearance('Pickup', color);
  const done = stopAppearance('Pickup', color, true);
  assert.deepEqual(done.fill, pending.border, 'the fill and the ring swap');
  assert.deepEqual(done.border, pending.fill);
  assert.deepEqual(done.text, color, 'and the number is the colour itself');
  assert.match(
    decodeURIComponent(stopMarkerIcon(done.fill, done.border, 13).url),
    /r="13" fill="rgb\(255,255,255\)" stroke="rgb\(32,122,99\)"/,
  );
  // A filled badge shows as the disc inside its white ring; an outlined one
  // shows as the ring itself, at the outer edge. At one radius the outlined
  // one reads as the larger, which is backwards for a stop already behind
  // the truck.
  assert.match(
    decodeURIComponent(stopMarkerIcon(pending.fill, pending.border).url),
    /r="15.5"/,
  );
});
