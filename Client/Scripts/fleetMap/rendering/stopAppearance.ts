import type { RouteColor } from './routePalette.ts';
import { sceneMetrics as metrics } from './sceneMetrics.ts';

export function isDelivery(job: string | null | undefined): boolean {
  return /^(delivery|dropoff)$/i.test((job || '').replace(/[\s_-]/g, ''));
}

// The instrument core every map mark shares: a glass disc under a fine rim
// of the mark's own colour - dark on the dark theme, pale on the light one,
// where the rim and letter carry the colour instead. The map is made again
// when the theme changes, so a mark reads the theme when it is drawn.
export const markCore = [11, 22, 38, 235];
export const lightMarkCore = [255, 255, 255, 240];
export const isLightMap = () =>
  globalThis.document?.documentElement?.dataset?.theme !== 'dark';

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
  const core = isLightMap() ? lightMarkCore : markCore;
  return done
    ? {
        fill: [...core.slice(0, 3), 200],
        border: [...color.slice(0, 3), 150],
        text: [...color.slice(0, 3), 170],
      }
    : { fill: [...core], border: accent, text: accent };
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
  // The badge's number: a long one widens the round badge into a pill of
  // the same height (the owner, September 28).
  label = '',
  // Pickup ('in') or delivery ('out'): a small icon of cargo going into or
  // out of the box, on the badge's lower right, in the badge's colour.
  load: 'in' | 'out' | null = null,
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
  // How much wider than round the words need the badge to be.
  const extra = Math.max(0, badgeTextWidth(label) + 12 - radius * 2);
  const width = span + extra;
  // A rounded rectangle as wide as the words; round when they fit.
  const pill = (inset: number, attributes: string) =>
    `<rect x="${inset}" y="${inset}" width="${width - inset * 2}" height="${span - inset * 2}" rx="${half - inset}" ${attributes}/>`;
  // A fine rim; a quiet (translucent) rim is a stop behind the truck, drawn
  // dashed so it reads as done even at a glance.
  const quiet = alpha(border) < 1;
  const badge = pill(
    half - radius,
    `fill="${paint(color)}" fill-opacity="${alpha(color).toFixed(2)}" stroke="${paint(border)}" stroke-opacity="${alpha(border).toFixed(2)}" stroke-width="${isLightMap() ? 2 : 1.75}"${quiet ? ' stroke-dasharray="2.2 1.6"' : ''}`,
  );
  const around = ring
    ? pill(
        1.25,
        `fill="${paint(ring)}" stroke="rgb(${markCore.slice(0, 3).join(',')})" stroke-width="1.5"`,
      )
    : '';
  // A badge drawn over a truck it has not reached yet stands on a dark
  // rim, so the disc behind it reads as a truck and not a smudge. Neither
  // mark may be moved to make room: the gap between them is how far the
  // truck still has to go.
  const halo = stacked
    ? pill(half - radius - 2.5, `fill="rgb(${markCore.slice(0, 3).join(',')})"`)
    : '';
  // Room around the badge for the icon, the same on every side so the
  // badge's centre stays the icon's anchor.
  const margin = load ? 5 : 0;
  const outerWidth = width + margin * 2,
    outerHeight = span + margin * 2;
  const iconX = margin + width - radius * 0.35 - extra / 2 - (half - radius),
    iconY = margin + span - (half - radius) - 2;
  const glyph =
    load === 'in'
      ? 'M3.2 6.2v3.3h5.6V6.2M6 1.6v4.6M4.3 4.4 6 6.2l1.7-1.8'
      : 'M3.2 6.2v3.3h5.6V6.2M6 6.2V1.6M4.3 3.4 6 1.6l1.7 1.8';
  const icon = load
    ? `<g transform="translate(${iconX - 6} ${iconY - 6})"><circle cx="6" cy="6" r="6" fill="${paint(border)}" stroke="${paint(color)}" stroke-width="1.2"/><path d="${glyph}" fill="none" stroke="${isLightMap() ? '#ffffff' : paint(color)}" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></g>`
    : '';
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${outerWidth * 4}" height="${outerHeight * 4}" viewBox="0 0 ${outerWidth} ${outerHeight}"><g transform="translate(${margin} ${margin})">${around}${halo}${badge}</g>${icon}</svg>`;
  return {
    url: `data:image/svg+xml,${encodeURIComponent(svg)}`,
    width: outerWidth * 4,
    height: outerHeight * 4,
    anchorX: outerWidth * 2,
    anchorY: outerHeight * 2,
    mask: false,
  };
}
import { currentRouteColor } from './routePalette.ts';

// A badge's words at the badge font (13 px bold): wide enough to hold
// "12 · D" without measuring text on every frame.
function badgeTextWidth(label: string) {
  let width = 0;
  for (const character of label)
    width += /[0-9]/.test(character) ? 7.3 : /[A-Z]/.test(character) ? 9 : 3.7;
  return width;
}
