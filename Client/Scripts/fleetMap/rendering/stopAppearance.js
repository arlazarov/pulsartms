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

export function stopMarkerIcon(color, border = [255, 255, 255, 255]) {
  const paint = value => `rgb(${value.slice(0, 3).join(',')})`;
  const fill = paint(color);
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="136" height="136" viewBox="0 0 34 34"><circle cx="17" cy="17" r="15.5" fill="${fill}" stroke="${paint(border)}" stroke-width="2.5"/></svg>`;
  return {
    url: `data:image/svg+xml,${encodeURIComponent(svg)}`,
    width: 136,
    height: 136,
    anchorX: 68,
    anchorY: 68,
    mask: false,
  };
}
import { currentRouteColor } from './routePalette.js';
