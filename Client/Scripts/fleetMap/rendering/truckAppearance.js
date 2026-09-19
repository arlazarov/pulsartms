const icons = new Map();
export function truckIcon(engine, speed = 0) {
  const state = typeof engine === 'string' ? engine.trim().toLowerCase() : '';
  const key =
    Number.isFinite(speed) && speed >= 1
      ? 'moving'
      : ['on', 'running', 'idle', 'idling'].includes(state)
        ? 'idle'
        : 'off';
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
<circle cx="13" cy="13" r="11" fill="${key === 'idle' ? '#16a34a' : '#64748b'}" stroke="#1e293b" stroke-width="2"/>`;
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
