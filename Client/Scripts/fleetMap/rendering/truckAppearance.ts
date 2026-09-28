import { isLightMap } from './stopAppearance.ts';

const icons = new Map<string, unknown>();

// Two facts, each told one way (the owner, September 27). The shape says
// motion: an arrow for a truck moving, a circle for one standing - the
// speed says that. The accent says the engine, from the telemetry's own
// engine reading and never from speed: a subtle green edge for on (idling
// included), a neutral edge for off, and a dashed neutral edge when the
// reading is missing or not one we know. The body is one neutral ink.
export type TruckMotion = 'moving' | 'standing';
export type TruckEngine = 'on' | 'off' | 'unknown';

export function truckMotion(speed = 0): TruckMotion {
  return Number.isFinite(speed) && speed >= 1 ? 'moving' : 'standing';
}

export function truckEngine(engine: unknown): TruckEngine {
  const state = typeof engine === 'string' ? engine.trim().toLowerCase() : '';
  return ['on', 'running', 'idle', 'idling'].includes(state)
    ? 'on'
    : state === 'off'
      ? 'off'
      : 'unknown';
}

// Kept for the sizes and rings that read it: moving, or standing with the
// engine on (idle) or not (off).
export function truckState(engine: unknown, speed = 0): string {
  return truckMotion(speed) === 'moving'
    ? 'moving'
    : truckEngine(engine) === 'on'
      ? 'idle'
      : 'off';
}

// The ring a stop badge wears while a truck stands on it says what the
// truck's mark says: on the dark map its engine, as the truck's edge does;
// on the light map, the classic marks' colour (green running, grey off).
export function truckColor(engine: unknown, speed = 0): string {
  if (isLightMap())
    return truckState(engine, speed) === 'off' ? ringQuiet : engineOn;
  return truckEngine(engine) === 'on' ? engineOn : ringQuiet;
}

const engineOn = '#16a34a';
// The quiet edge is darker than the quiet ring: it is read against the
// dark map's pale truck body, the ring against the map.
const engineQuiet = '#475569';
const ringQuiet = '#64748b';
const body = '#1e293b';
// The dark map's instrument marks: the glass body by engine state, the
// casing and the spine.
const glassOn = { lit: '#dcfce7', deep: '#4ade80' };
const glassOff = { lit: '#cffafe', deep: '#22d3ee' };
const glassUnknown = { lit: '#f1f5f9', deep: '#94a3b8' };
const offGlow = '#22d3ee';
const ringOn = '#4ade80';
const casing = '#020617';
const spine = '#0b1626';

// The light map keeps the classic truck marks (the owner, September 27): a
// green arrow moving, a green circle standing with the engine on, a grey one
// with it off. The engine-edge instrument marks are the dark map's.
function lightTruckIcon(engine: unknown, speed: number) {
  const key = `light:${truckState(engine, speed)}`;
  if (!icons.has(key)) {
    const silhouette = 'M13 1 L24 23 Q25 26 22 25 L13 22 L4 25 Q1 26 2 23 Z';
    const shape =
      key === 'light:moving'
        ? `
<path d="${silhouette}" fill="none" stroke="white" stroke-width="4" stroke-linejoin="round"/>
<path d="${silhouette}" fill="${engineOn}" stroke="${body}" stroke-width="2" stroke-linejoin="round"/>`
        : `
<circle cx="13" cy="13" r="11" fill="none" stroke="white" stroke-width="4"/>
<circle cx="13" cy="13" r="11" fill="${key === 'light:idle' ? engineOn : ringQuiet}" stroke="${body}" stroke-width="2"/>`;
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="112" height="120" viewBox="-1 -1 28 30">${shape}</svg>`;
    icons.set(key, {
      url: 'data:image/svg+xml,' + encodeURIComponent(svg),
      width: 112,
      height: 120,
      anchorX: 56,
      anchorY: 56,
      mask: false,
    });
  }
  return icons.get(key);
}

// The dark map's marks: the engine-edge truck drawn as an instrument that
// stands out from the dark ground (the owner, September 27: dark bodies
// sank into the map) - a bright glass body in a dark casing with a fine
// dark spine or core, green with a green glow while the engine runs, cyan
// with a cyan glow while it is off.
export function truckIcon(engine: unknown, speed = 0) {
  if (isLightMap()) return lightTruckIcon(engine, speed);
  const motion = truckMotion(speed);
  const reading = truckEngine(engine);
  const key = `${motion}:${reading}`;
  if (!icons.has(key)) {
    // Preserve the original heading anchor; the unit label remains upright.
    const silhouette = 'M13 1 L24 23 Q25 26 22 25 L13 22 L4 25 Q1 26 2 23 Z';
    const edge =
      reading === 'on'
        ? `stroke="${engineOn}" stroke-width="2.5"`
        : reading === 'off'
          ? `stroke="${engineQuiet}" stroke-width="2"`
          : `stroke="${engineQuiet}" stroke-width="2" stroke-dasharray="3 2"`;
    // The engine is told by the whole mark, not a thin edge alone: running,
    // green glass with a green glow; off, bright cyan glass with a cyan
    // glow, so a standing truck with its engine off still stands out from
    // the dark map; unknown, grey glass with a dashed edge.
    const tone =
      reading === 'on' ? glassOn : reading === 'off' ? glassOff : glassUnknown;
    const glass = `<defs><linearGradient id="b" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${tone.lit}"/><stop offset="1" stop-color="${tone.deep}"/></linearGradient></defs>`;
    // The engine's colour, bright, for the glow and the sight ring.
    const ring = reading === 'on' ? ringOn : offGlow;
    const glow = `stroke="${ring}" stroke-opacity="0.7"`;
    const shape =
      motion === 'moving'
        ? `
<path d="${silhouette}" fill="none" ${glow} stroke-width="8" stroke-linejoin="round"/>
<path d="${silhouette}" fill="none" stroke="${casing}" stroke-opacity="0.9" stroke-width="3.5" stroke-linejoin="round"/>
<path d="${silhouette}" fill="url(#b)" ${edge} stroke-linejoin="round"/>
<path d="M13 6.5 L13 17.5" stroke="${spine}" stroke-width="1.3" stroke-linecap="round" stroke-opacity="0.75"/>`
        : // Standing: a HUD sight - a bright ring of the engine's colour in
          // a soft glow around a pale glass core, so it reads as a truck
          // and not as a map button.
          `
<circle cx="13" cy="13" r="12.4" fill="none" stroke="${ring}" stroke-opacity="0.45" stroke-width="3.4"/>
<circle cx="13" cy="13" r="12.4" fill="none" stroke="${ring}" stroke-width="2"/>
<circle cx="13" cy="13" r="8.6" fill="none" stroke="${casing}" stroke-opacity="0.9" stroke-width="3"/>
<circle cx="13" cy="13" r="8.6" fill="url(#b)" ${edge}/>
<circle cx="13" cy="13" r="2.2" fill="${spine}"/>`;
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="112" height="120" viewBox="-1 -1 28 30">${glass}${shape}</svg>`;
    icons.set(key, {
      url: 'data:image/svg+xml,' + encodeURIComponent(svg),
      width: 112,
      height: 120,
      anchorX: 56,
      anchorY: 56,
      mask: false,
    });
  }
  return icons.get(key);
}

// The disc a truck is picked by: drawn, but nearly transparent, so the
// picking pass sees all of it and the eye nothing.
const hitIcon = {
  url:
    'data:image/svg+xml,' +
    encodeURIComponent(
      '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64">' +
        '<circle cx="32" cy="32" r="32" fill="#000" fill-opacity="0.01"/></svg>',
    ),
  width: 64,
  height: 64,
  anchorX: 32,
  anchorY: 32,
  mask: false,
};

export function truckHitIcon() {
  return hitIcon;
}
