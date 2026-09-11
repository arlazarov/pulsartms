/** @param {string | null | undefined} job */
export function isDelivery(job) {
  return /^(delivery|dropoff)$/i.test((job || '').replace(/[\s_-]/g, ''));
}

/** @param {string | null | undefined} job @param {readonly number[]} [color] */
export function stopAppearance(job, color = currentRouteColor) {
  const accent = [...color.slice(0, 3), 255];
  const white = [255, 255, 255, 255];
  return { fill: accent, border: white, text: white };
}

export function stopMarkerIcon(color) {
  const fill = `rgb(${color.slice(0, 3).join(',')})`;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="136" height="136" viewBox="0 0 34 34"><circle cx="17" cy="17" r="15.5" fill="${fill}" stroke="white" stroke-width="2.5"/></svg>`;
  return { url: `data:image/svg+xml,${encodeURIComponent(svg)}`, width: 136, height: 136,
    anchorX: 68, anchorY: 68, mask: false };
}
import { currentRouteColor } from './routePalette.js';
