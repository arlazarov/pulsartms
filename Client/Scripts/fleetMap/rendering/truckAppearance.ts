const icons = new Map<string, unknown>();

// Moving, standing with the engine on, standing with it off - and the colour
// that says which. The ring a badge wears while a truck stands on it is
// painted this colour too: the ring is the truck, so it says what the truck
// says - green with the engine running, grey with it off.
export function truckState(engine: unknown, speed = 0): string {
  const state = typeof engine === 'string' ? engine.trim().toLowerCase() : '';
  return Number.isFinite(speed) && speed >= 1
    ? 'moving'
    : ['on', 'running', 'idle', 'idling'].includes(state)
      ? 'idle'
      : 'off';
}

export function truckColor(engine: unknown, speed = 0): string {
  return truckState(engine, speed) === 'off' ? truckStopped : truckRunning;
}

const truckRunning = '#16a34a';
const truckStopped = '#64748b';

export function truckIcon(engine: unknown, speed = 0) {
  const key = truckState(engine, speed);
  if (!icons.has(key)) {
    // Preserve the original heading anchor; the unit label remains upright.
    const silhouette = 'M13 1 L24 23 Q25 26 22 25 L13 22 L4 25 Q1 26 2 23 Z';
    const shape =
      key === 'moving'
        ? `
<path d="${silhouette}" fill="none" stroke="white" stroke-width="4" stroke-linejoin="round"/>
<path d="${silhouette}" fill="#16a34a" stroke="#1e293b" stroke-width="2" stroke-linejoin="round"/>`
        : `
<circle cx="13" cy="13" r="11" fill="none" stroke="white" stroke-width="4"/>
<circle cx="13" cy="13" r="11" fill="${truckColor(key === 'idle' ? 'on' : 'off')}" stroke="#1e293b" stroke-width="2"/>`;
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
