/** @param {string | null | undefined} job */
export function isDelivery(job) {
  return /^(delivery|dropoff)$/i.test((job || '').replace(/[\s_-]/g, ''));
}

/** @param {string | null | undefined} job @param {readonly number[]} [color]
 * @param {boolean} [done] */
export function stopAppearance(job, color = currentRouteColor, done = false) {
  const accent = [...color.slice(0, 3), 255];
  const white = [255, 255, 255, 255];
  // A stop behind the truck is outlined, not filled: it no longer asks for
  // anything. The card has said it that way all along; the map said it with
  // the same filled circle as the stop still to come.
  return done
    ? { fill: white, border: accent, text: accent }
    : { fill: accent, border: white, text: white };
}

// A filled badge shows as the disc inside its white ring; an outlined one
// shows as the ring itself, at the outer edge. Drawn at the same radius the
// outlined badge therefore reads as the larger and louder of the two, which
// is backwards for a stop already behind the truck. Its ring is drawn where
// the filled badge's edge is instead.
// A truck standing on a stop is not a second mark beside it: the badge is
// drawn inside a ring of the truck's colour, one mark on one point saying
// both things. Two marks on one point meant one of them had to be moved off
// the place it names, and whichever was moved then pointed at nothing.
export function stopMarkerIcon(
  color,
  border = [255, 255, 255, 255],
  radius = 15.5,
  ring = null,
) {
  const paint = value =>
    typeof value === 'string' ? value : `rgb(${value.slice(0, 3).join(',')})`;
  const span = ring ? 46 : 34;
  const half = span / 2;
  // Inside a ring a filled badge keeps a thinner white edge: at full width
  // it left a band of the truck's colour too narrow to see, and dropped
  // altogether the two touched - and a stop on a teal route inside a green
  // ring is one blot. An outlined badge is its edge, and keeps all of it.
  const edge = ring && paint(border) === 'rgb(255,255,255)' ? 1.5 : 2.5;
  const badge = `<circle cx="${half}" cy="${half}" r="${radius}" fill="${paint(color)}" stroke="${paint(border)}" stroke-width="${edge}"/>`;
  const around = ring
    ? `<circle cx="${half}" cy="${half}" r="${half - 1.25}" fill="${paint(ring)}" stroke="rgb(255,255,255)" stroke-width="2.5"/>`
    : '';
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${span * 4}" height="${span * 4}" viewBox="0 0 ${span} ${span}">${around}${badge}</svg>`;
  return {
    url: `data:image/svg+xml,${encodeURIComponent(svg)}`,
    width: span * 4,
    height: span * 4,
    anchorX: span * 2,
    anchorY: span * 2,
    mask: false,
  };
}
import { currentRouteColor } from './routePalette.js';
