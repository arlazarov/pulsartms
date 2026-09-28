// One store for both screens: session, shared feeds, selection and UI
// state. Feeds keep their last good result with its time so a failed poll
// shows stale data honestly instead of blanking or pretending it is live.

const listeners = new Set();
let frame = 0;

function readPref(key, fallback) {
  try {
    return localStorage.getItem(key) ?? fallback;
  } catch {
    return fallback;
  }
}

export function writePref(key, value) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Preference only; ignore blocked storage.
  }
}

const feed = () => ({ data: null, state: 'idle', at: 0, error: null });

export const state = {
  route: 'fleet',
  theme: readPref('pulsr-concept-theme',
    matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'),
  leftOpen: readPref('pulsr-concept-left', 'open') === 'open',
  rightOpen: true,
  mobileSheet: 'none',
  following: false,
  tab: 'route',
  filter: 'all',
  query: '',
  dispatchFilter: 'all',
  dispatchPage: 1,
  dockView: 'chain',
  layers: { others: true, future: true, cities: true },
  selection: { truckId: null, dispatchId: null, stopId: null },
  expanded: new Set(),
  collapsed: new Set(),
  feeds: {
    locations: feed(),
    hos: feed(),
    board: feed(),
    boardPlanning: feed(),
    planning: feed(),
    next: feed(),
  },
};

export function resetFeeds() {
  for (const k of Object.keys(state.feeds)) state.feeds[k] = feed();
}

export function subscribe(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function update(mutator) {
  mutator?.(state);
  if (frame) return;
  frame = requestAnimationFrame(() => {
    frame = 0;
    for (const fn of listeners) fn(state);
  });
}

export function select(next) {
  update((s) => {
    const sel = s.selection;
    const truckChanged =
      next.truckId !== undefined && next.truckId !== sel.truckId;
    s.selection = {
      truckId: next.truckId !== undefined ? next.truckId : sel.truckId,
      dispatchId:
        next.dispatchId !== undefined
          ? next.dispatchId
          : truckChanged
            ? null
            : sel.dispatchId,
      stopId:
        next.stopId !== undefined
          ? next.stopId
          : truckChanged || next.dispatchId !== undefined
            ? null
            : sel.stopId,
    };
    if (truckChanged) {
      s.feeds.planning = feed();
      s.feeds.next = feed();
      s.expanded = new Set();
      s.collapsed = new Set();
    }
    if (next.dispatchId) s.expanded.add(next.dispatchId);
  });
  syncHash();
}

export function syncHash() {
  const p = new URLSearchParams();
  const { truckId, dispatchId } = state.selection;
  if (truckId) p.set('truck', truckId);
  if (dispatchId) p.set('load', dispatchId);
  const q = p.toString();
  const hash = `#/${state.route}${q ? `?${q}` : ''}`;
  if (location.hash !== hash) history.replaceState(null, '', hash);
}
