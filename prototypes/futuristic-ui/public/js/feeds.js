// Polling that mirrors the maintained pages' cadence (Client RefreshLoop):
// pause while the tab is hidden, run at once when it is shown again, and
// stop when the page scope ends. Intervals match FleetMap/DispatchList.
import { request } from './api.js';
import { state, update } from './store.js';

const BOARD_PAGE = 12;
const MAX_BOARD_PAGES = 9;
const today = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(
    d.getDate()
  ).padStart(2, '0')}`;
};

export function createScope() {
  const controller = new AbortController();
  const stops = [];
  return {
    signal: controller.signal,
    add: (fn) => stops.push(fn),
    dispose() {
      controller.abort();
      for (const fn of stops.splice(0)) fn();
    },
  };
}

// Runs `load` now and then every `interval()` ms while the tab is visible.
export function poll(scope, name, load, interval) {
  let timer = 0;
  let inflight = null;
  const run = () => {
    if (inflight) return inflight;
    clearTimeout(timer);
    if (scope.signal.aborted) return Promise.resolve();
    inflight = (async () => {
      if (document.visibilityState === 'visible')
        await refresh(name, () => load(scope.signal), scope.signal);
    })().finally(() => {
      inflight = null;
      if (!scope.signal.aborted)
        timer = setTimeout(run, typeof interval === 'function'
          ? interval()
          : interval);
    });
    return inflight;
  };
  const onVisible = () => {
    if (document.visibilityState === 'visible') run();
  };
  document.addEventListener('visibilitychange', onVisible);
  scope.add(() => {
    clearTimeout(timer);
    document.removeEventListener('visibilitychange', onVisible);
  });
  run();
  return run;
}

async function refresh(name, load, signal) {
  const f = state.feeds[name];
  if (!f.data) update((s) => (s.feeds[name].state = 'loading'));
  try {
    const data = await load();
    if (signal.aborted) return;
    update((s) => {
      s.feeds[name] = { data, state: 'live', at: Date.now(), error: null };
    });
  } catch (e) {
    if (signal.aborted || e.name === 'AbortError') return;
    update((s) => {
      const cur = s.feeds[name];
      s.feeds[name] = {
        ...cur,
        state: cur.data ? 'stale' : 'unavailable',
        error: e.message,
      };
    });
  }
}

export const loaders = {
  locations: (signal) =>
    request('GET', '/api/fleet/locations', null, signal),
  hos: (signal) => request('GET', '/api/fleet/hos', null, signal),
  // The board projection, read page by page at the page size the
  // maintained board uses, until every truck row is loaded.
  async board(signal) {
    const items = [];
    for (let page = 1; page <= MAX_BOARD_PAGES; page++) {
      const q = new URLSearchParams({
        page,
        pageSize: BOARD_PAGE,
        search: '',
        date: today(),
        includePlanned: 'true',
        includeHos: 'false',
        includeFinancials: 'false',
        includeEta: 'false',
      });
      const r = await request('GET', `/api/dispatch/board?${q}`, null, signal);
      items.push(...(r?.items ?? []));
      if (!r?.hasNextPage && page >= (r?.totalPages ?? 1)) break;
    }
    return items;
  },
  async boardPlanning(signal) {
    const pages = Math.max(
      1,
      Math.ceil((state.feeds.board.data?.length ?? 1) / BOARD_PAGE)
    );
    const all = [];
    for (let page = 1; page <= Math.min(pages, MAX_BOARD_PAGES); page++) {
      const r = await request(
        'POST',
        '/api/dispatch/board/planning',
        { page, search: '', truckId: null, date: today() },
        signal
      );
      all.push(...(r ?? []));
    }
    return all;
  },
  // Selected truck: the Fleet Map's planning read. Geometry is omitted by
  // the server when the known plan version is unchanged.
  async planning(signal) {
    const truckId = state.selection.truckId;
    if (!truckId) return null;
    const prev = state.feeds.planning.data;
    const plan = prev?.truckId === truckId ? prev.state?.plan : null;
    const q = plan?.id
      ? `?knownPlanId=${plan.id}&knownVersion=${plan.version}`
      : '';
    const r = await request(
      'POST',
      `/api/fleet/trucks/${truckId}/planning${q}`,
      {},
      signal
    );
    if (r?.state?.plan?.geometryOmitted && plan) {
      r.state.plan.route = plan.route;
      r.state.plan.stops ??= plan.stops;
    }
    return r ? { ...r, truckId } : { truckId, state: null };
  },
  async next(signal) {
    const truckId = state.selection.truckId;
    const planning = state.feeds.planning.data;
    if (!truckId || planning?.truckId !== truckId) return null;
    const prev = state.feeds.next.data;
    const q = new URLSearchParams();
    if (planning.dispatchId) q.set('currentDispatchId', planning.dispatchId);
    if (planning.executionLegId)
      q.set('currentExecutionLegId', planning.executionLegId);
    if (prev?.truckId === truckId && prev.revision !== undefined)
      q.set('revision', prev.revision);
    const r = await request(
      'GET',
      `/api/dispatch/truck/${truckId}/next-routes?${q}`,
      null,
      signal
    );
    if (r?.unchanged && prev?.truckId === truckId)
      return { ...prev, revision: r.revision };
    return { ...r, truckId };
  },
};

export function startShared(scope) {
  poll(scope, 'locations', loaders.locations, 10000);
  poll(scope, 'hos', loaders.hos, 15000);
  const board = poll(scope, 'board', loaders.board, 60000);
  poll(scope, 'boardPlanning', loaders.boardPlanning, () => {
    const rows = state.feeds.board.data ?? [];
    const plans = state.feeds.boardPlanning.data ?? [];
    const missing = rows.some(
      (r) =>
        r.truckId &&
        r.dispatches?.length &&
        !plans.find((p) => p.truckId === r.truckId && p.state?.plan)
    );
    return missing ? 10000 : 60000;
  });
  return { board };
}

export function startSelected(scope) {
  const planning = poll(scope, 'planning', loaders.planning, 10000);
  const next = poll(scope, 'next', loaders.next, 30000);
  return { planning, next };
}
