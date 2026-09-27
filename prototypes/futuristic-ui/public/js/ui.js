// Shared markup helpers and icons used by both screens.
export const esc = (v) =>
  String(v ?? '').replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[
      c
    ]
  );

const paths = {
  map: '<path d="M12 21s-6-5.3-6-10.5A6 6 0 0 1 18 10.5C18 15.7 12 21 12 21z"/><circle cx="12" cy="10.5" r="2.2"/>',
  board: '<rect x="4" y="4" width="16" height="16" rx="2.5"/><path d="M4 9.5h16M9.5 9.5V20"/>',
  chat: '<path d="M5 5h14a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H10l-4 3.5V16H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z"/>',
  users: '<circle cx="9" cy="8.5" r="3"/><path d="M3.5 19c.6-3 2.8-4.6 5.5-4.6s4.9 1.6 5.5 4.6"/><path d="M15.5 5.8a3 3 0 0 1 0 5.4M17.5 14.6c1.6.6 2.7 2 3 4.4"/>',
  search: '<circle cx="11" cy="11" r="6.5"/><path d="m20 20-4.2-4.2"/>',
  route: '<circle cx="6" cy="18" r="2"/><circle cx="18" cy="6" r="2"/><path d="M8 18h7a3 3 0 0 0 0-6H9a3 3 0 0 1 0-6h7"/>',
  fuel: '<path d="M5 20V5.5A1.5 1.5 0 0 1 6.5 4h6A1.5 1.5 0 0 1 14 5.5V20M4 20h11M5 10h9"/><path d="M14 8.5h1.8a1.5 1.5 0 0 1 1.5 1.5v6a1.3 1.3 0 0 0 2.6 0V9.2L17.5 6.8"/>',
  truck: '<path d="M3 6.5h10.5V16H3zM13.5 9.5h4l3 3.2V16h-7"/><circle cx="7" cy="17" r="1.8"/><circle cx="17" cy="17" r="1.8"/>',
  user: '<circle cx="12" cy="8.5" r="3.5"/><path d="M5 20c.8-3.6 3.6-5.5 7-5.5s6.2 1.9 7 5.5"/>',
  trailer: '<path d="M2.5 7h15v8.5h-15zM17.5 13h3"/><circle cx="7" cy="17.2" r="1.6"/><circle cx="12" cy="17.2" r="1.6"/>',
  box: '<path d="m12 3 8 4.5v9L12 21l-8-4.5v-9z"/><path d="m4 7.5 8 4.5 8-4.5M12 12v9"/>',
  clock: '<circle cx="12" cy="12" r="8"/><path d="M12 7.5V12l3 2"/>',
  gauge: '<path d="M4.5 16a8 8 0 1 1 15 0"/><path d="m12 13 3.5-4"/>',
  wheel: '<circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="2"/><path d="M12 4v6M5 15.5l5.3-2.7M19 15.5l-5.3-2.7"/>',
  chevron: '<path d="m6 9 6 6 6-6"/>',
  right: '<path d="M5 12h14M13 6l6 6-6 6"/>',
  check: '<path d="m5 12.5 4.2 4L19 7"/>',
  alert: '<path d="M12 4 2.8 19.5h18.4z"/><path d="M12 10v4.5M12 17.2v.1"/>',
  sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4"/>',
  moon: '<path d="M19.5 14.5A7.8 7.8 0 0 1 9.5 4.5a7.8 7.8 0 1 0 10 10z"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  minus: '<path d="M5 12h14"/>',
  layers: '<path d="m12 4 8.5 4.5L12 13 3.5 8.5z"/><path d="m3.5 12.5 8.5 4.5 8.5-4.5M3.5 16.5 12 21l8.5-4.5"/>',
  compass: '<path d="m12 3 5 16-5-3.5L7 19z"/>',
  fit: '<path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/>',
  panelLeft: '<rect x="3.5" y="4.5" width="17" height="15" rx="2"/><path d="M9.5 4.5v15"/>',
  panelRight: '<rect x="3.5" y="4.5" width="17" height="15" rx="2"/><path d="M14.5 4.5v15"/>',
  close: '<path d="M6 6l12 12M18 6 6 18"/>',
  list: '<path d="M8 6h12M8 12h12M8 18h12M4 6h.1M4 12h.1M4 18h.1"/>',
  filter: '<path d="M4 5h16l-6 7.5V19l-4-2v-4.5z"/>',
  logout: '<path d="M14 4h4.5A1.5 1.5 0 0 1 20 5.5v13a1.5 1.5 0 0 1-1.5 1.5H14M10 16l-4-4 4-4M6 12h10"/>',
  more: '<circle cx="5.5" cy="12" r="1.2"/><circle cx="12" cy="12" r="1.2"/><circle cx="18.5" cy="12" r="1.2"/>',
  pin: '<path d="M12 21s-6-5.3-6-10.5A6 6 0 0 1 18 10.5C18 15.7 12 21 12 21z"/><circle cx="12" cy="10.5" r="2.2"/>',
  timeline: '<path d="M4 7h9M7 12h11M5 17h7"/><path d="M4 4v16" opacity=".5"/>',
};

export const icon = (name, cls = '') =>
  `<svg class="icon ${cls}" viewBox="0 0 24 24" aria-hidden="true">${
    paths[name] ?? ''}</svg>`;

export const wordmark = (cls = 'wordmark') =>
  `<svg class="${cls}" viewBox="0 0 480 104" role="img" aria-label="PulsR TMS"><use href="assets/pulsr.svg#wordmark"/></svg>`;

export const pulseIcon = () =>
  `<svg class="pulse-icon" viewBox="0 0 128 128" aria-hidden="true"><use href="assets/pulsr.svg#pulse" transform="translate(25 12)"/></svg>`;

export const phaseBadge = (phase, label) =>
  `<span class="phase ${phase}">${esc(label)}</span>`;

export function dataState(state, detail = '') {
  const labels = {
    live: 'Live',
    loading: 'Loading',
    stale: 'Stale',
    unavailable: 'Unavailable',
    demo: 'Demo fixtures · synthetic',
    idle: 'Waiting',
  };
  return `<span class="data-state" data-state="${state}" role="status"
    title="${esc(detail)}"><i></i>${state === 'demo'
      ? '<span class="ds-long">Demo fixtures · synthetic</span><span class="ds-short">Demo</span>'
      : esc(labels[state] ?? state)}${
    detail && state !== 'demo' ? ` <span class="muted">${esc(detail)}</span>`
      : ''}</span>`;
}

// Replace a region's markup while keeping focus on the same keyed control.
export function patch(host, html) {
  const active = document.activeElement;
  const key = host.contains(active) ? active?.dataset?.key : null;
  const scroll = [...host.querySelectorAll('[data-scroll]')].map((n) => [
    n.dataset.scroll,
    n.scrollTop,
    n.scrollLeft,
  ]);
  host.innerHTML = html;
  for (const [k, top, left] of scroll) {
    const n = host.querySelector(`[data-scroll="${k}"]`);
    if (n) {
      n.scrollTop = top;
      n.scrollLeft = left;
    }
  }
  if (key) host.querySelector(`[data-key="${CSS.escape(key)}"]`)?.focus();
}
