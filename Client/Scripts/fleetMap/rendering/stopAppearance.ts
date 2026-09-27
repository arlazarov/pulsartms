import type { RouteColor } from './routePalette.ts';
import { sceneMetrics as metrics } from './sceneMetrics.ts';

export function isDelivery(job: string | null | undefined): boolean {
  return /^(delivery|dropoff)$/i.test((job || '').replace(/[\s_-]/g, ''));
}

// The instrument core every map mark shares: a dark glass disc under a
// fine rim of the mark's own colour, in both themes.
export const markCore = [11, 22, 38, 235];

// How a stop's badge is painted: a dark glass core, a fine rim and the
// letter in its load's colour while it is still to come; once it is behind
// the truck the rim turns dashed and the letter quiet, so it no longer asks
// for anything.
export function stopAppearance(
  job: string | null | undefined,
  color: readonly number[] = currentRouteColor,
  done = false,
): { fill: number[]; border: number[]; text: number[] } {
  const accent = [...color.slice(0, 3), 255];
  return done
    ? {
        fill: [...markCore.slice(0, 3), 200],
        border: [...color.slice(0, 3), 150],
        text: [...color.slice(0, 3), 170],
      }
    : { fill: [...markCore], border: accent, text: accent };
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
  color: readonly number[],
  border: readonly number[] = [255, 255, 255, 255],
  radius = metrics.stopBadgeDiameter / 2 - 1.5,
  // The colour of the truck standing on this stop, when one is; the badge
  // is then drawn as a ring in that colour around it.
  ring: string | readonly number[] | null = null,
  stacked = false,
) {
  const paint = (value: string | readonly number[]) =>
    typeof value === 'string' ? value : `rgb(${value.slice(0, 3).join(',')})`;
  const alpha = (value: string | readonly number[]) =>
    typeof value === 'string' ? 1 : (value[3] ?? 255) / 255;
  const span = ring
    ? metrics.stopBadgeStandingDiameter
    : stacked
      ? metrics.stopBadgeStackedDiameter
      : metrics.stopBadgeDiameter;
  const half = span / 2;
  // A fine rim; a quiet (translucent) rim is a stop behind the truck, drawn
  // dashed so it reads as done even at a glance.
  const quiet = alpha(border) < 1;
  const badge = `<circle cx="${half}" cy="${half}" r="${radius}" fill="${paint(color)}" fill-opacity="${alpha(color).toFixed(2)}" stroke="${paint(border)}" stroke-opacity="${alpha(border).toFixed(2)}" stroke-width="1.75"${quiet ? ' stroke-dasharray="2.2 1.6"' : ''}/>`;
  const around = ring
    ? `<circle cx="${half}" cy="${half}" r="${half - 1.25}" fill="${paint(ring)}" stroke="rgb(${markCore.slice(0, 3).join(',')})" stroke-width="1.5"/>`
    : '';
  // A badge drawn over a truck it has not reached yet stands on a dark
  // rim, so the disc behind it reads as a truck and not a smudge. Neither
  // mark may be moved to make room: the gap between them is how far the
  // truck still has to go.
  const halo = stacked
    ? `<circle cx="${half}" cy="${half}" r="${radius + 2.5}" fill="rgb(${markCore.slice(0, 3).join(',')})"/>`
    : '';
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${span * 4}" height="${span * 4}" viewBox="0 0 ${span} ${span}">${around}${halo}${badge}</svg>`;
  return {
    url: `data:image/svg+xml,${encodeURIComponent(svg)}`,
    width: span * 4,
    height: span * 4,
    anchorX: span * 2,
    anchorY: span * 2,
    mask: false,
  };
}
import { currentRouteColor } from './routePalette.ts';
