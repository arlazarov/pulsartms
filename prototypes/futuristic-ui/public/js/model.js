// Joins the shared feeds into the truck/trip view both screens render.
// Only formatting and joining happen here; every fact comes from a server
// projection and every label from rules.js.
import { state } from './store.js';
import {
  destination,
  hosClocks,
  isLowClock,
  loadPhase,
  loadStatus,
  location,
  motionLabel,
  orderedStops,
  origin,
  rigStatus,
  text,
} from './rules.js';

let memo = { key: null, value: null };

export function fleet() {
  const f = state.feeds;
  const key = [f.locations.at, f.hos.at, f.board.at, f.boardPlanning.at,
    f.planning.at].join(':');
  if (memo.key === key) return memo.value;
  memo = { key, value: build() };
  return memo.value;
}

function build() {
  const f = state.feeds;
  const located = f.locations.data?.trucks ?? [];
  const hos = f.hos.data ?? {};
  const rows = f.board.data ?? [];
  const summaries = f.boardPlanning.data ?? [];
  const byId = new Map();

  for (const l of located) {
    byId.set(l.truckId, {
      id: l.truckId,
      unit: l.unitNumber ?? '—',
      driver: l.driverName,
      trailer: l.trailerNumber,
      trailerConflict: l.trailerConflictNumber,
      lat: l.latitude,
      lng: l.longitude,
      heading: l.heading,
      speed: l.speed,
      engine: l.engineState,
      updatedAt: l.updatedAt,
      place: l.formattedLocation,
      fuelPercent: l.fuelPercent,
      dispatches: [],
    });
  }
  for (const r of rows) {
    if (!r.truckId) continue;
    const t = byId.get(r.truckId) ?? {
      id: r.truckId,
      unit: r.truckNumber ?? '—',
      speed: r.speed,
      engine: r.engineState,
      dispatches: [],
    };
    t.driver ??= r.driverName;
    t.trailer ??= r.trailerNumber;
    t.boardKey = r.key;
    t.dispatches = r.dispatches ?? [];
    byId.set(r.truckId, t);
  }
  const unassigned = rows
    .filter((r) => !r.truckId)
    .flatMap((r) => r.dispatches ?? []);

  const trucks = [...byId.values()].map((t) => {
    const h = hos[t.id];
    t.driver = text(t.driver, h?.driverName);
    t.hos = h?.hos ?? null;
    t.rig = rigStatus(t.speed, t.engine, t.hos);
    t.motion = motionLabel(t.rig, t.speed);
    t.moving = t.rig === 'moving';
    t.summary =
      f.planning.data?.truckId === t.id && f.planning.data.state !== null
        ? f.planning.data
        : summaries.find((s) => s.truckId === t.id) ?? null;
    t.trips = t.dispatches.map((d, i) => trip(t, d, i));
    t.attention = attention(t);
    return t;
  });
  trucks.sort((a, b) =>
    String(a.unit).localeCompare(String(b.unit), undefined, { numeric: true })
  );
  return { trucks, unassigned };
}

function trip(t, d, i) {
  const phase = loadPhase(t.dispatches, d, t.summary?.dispatchId);
  const from = origin(d);
  const to = destination(d);
  return {
    id: d.id,
    index: i + 1,
    load: d,
    loadNumber: text(d.loadNumber),
    phase,
    status: loadStatus(d),
    from,
    to,
    fromText: location(from),
    toText: location(to),
    stops: orderedStops(d),
  };
}

// Attention items are server facts the maintained UI already surfaces:
// low HOS clocks (DriverHours is-low), trailer conflicts, planning notices,
// off-route / stale GPS warnings and late ETA stops.
function attention(t) {
  const out = [];
  for (const [name, ms] of hosClocks(t.hos))
    if (isLowClock(ms))
      out.push({ level: 'warn', kind: 'hos', text: `${name} clock under 1h` });
  if (t.trailerConflict)
    out.push({
      level: 'warn',
      kind: 'trailer',
      text: `Trailer conflict · ${t.trailerConflict}`,
    });
  const s = t.summary;
  for (const n of s?.notices ?? [])
    out.push({ level: 'warn', kind: 'notice', text: n.text });
  const p = s?.state?.progress;
  if (p?.offRoute)
    out.push({ level: 'warn', kind: 'route', text: 'Off planned route' });
  if (p?.locationStale)
    out.push({
      level: 'warn',
      kind: 'gps',
      text: 'GPS unavailable or stale',
    });
  for (const e of s?.state?.eta?.stops ?? [])
    if (e.lateMinutes > 0)
      out.push({
        level: 'danger',
        kind: 'late',
        text: `Late ${e.lateMinutes} min at a stop`,
        stopId: e.stopId,
      });
  return out;
}

export const etaFor = (t, stopId) =>
  t?.summary?.state?.eta?.stops?.find((e) => e.stopId === stopId) ?? null;

export function selectedTruck() {
  const id = state.selection.truckId;
  return id ? fleet().trucks.find((t) => t.id === id) ?? null : null;
}

export function selectedTrip(t = selectedTruck()) {
  if (!t) return null;
  const id = state.selection.dispatchId;
  return (
    t.trips.find((x) => x.id === id) ??
    t.trips.find((x) => x.phase === 'current') ??
    t.trips[0] ??
    null
  );
}

export function matches(t, q) {
  if (!q) return true;
  const hay = [t.unit, t.driver, t.trailer,
    ...t.trips.map((x) => x.loadNumber)].join(' ').toLowerCase();
  return hay.includes(q.toLowerCase());
}

export function filterTrucks(trucks, filter, q) {
  return trucks.filter(
    (t) =>
      matches(t, q) &&
      (filter === 'all' ||
        (filter === 'moving' && t.moving) ||
        (filter === 'stopped' && !t.moving) ||
        (filter === 'attention' && t.attention.length > 0))
  );
}
