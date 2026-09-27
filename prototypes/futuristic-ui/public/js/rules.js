// Display rules ported one-to-one from the maintained Client so Fleet and
// Dispatch read server facts the same way the production pages do. Nothing
// here is a new business rule; each function names its source.

export function dateOnly(text) {
  if (!text) return null;
  const [y, m, d] = String(text).slice(0, 10).split('-').map(Number);
  return y ? new Date(y, m - 1, d) : null;
}

const firstStop = (load) =>
  [...(load.stops ?? [])].sort((a, b) => a.sequence - b.sequence)[0];

// Phase comes from server facts, not a client date rule: the current trip is
// the dispatch the server's planning result is for (AutomaticPlanningResult
// .dispatchId); the others are named by their place in the board's own order.
// Without a planning result no trip is called current.
export function loadPhase(dispatches, load, currentId) {
  if (load.completed) return 'completed';
  const index = dispatches.findIndex((d) => d.id === load.id);
  const cur = currentId ? dispatches.findIndex((d) => d.id === currentId) : -1;
  if (cur >= 0 && index === cur) return 'current';
  const after = cur >= 0 ? index - cur : index + 1;
  if (after === 1) return 'next';
  return 'upcoming';
}

export const phaseLabel = {
  current: 'Current',
  next: 'Next',
  upcoming: 'Upcoming',
  completed: 'Completed',
};

// Shared/Dispatch/DispatchBoardRow.cs Status
export function loadStatus(load) {
  const s = (load.status ?? '').toLowerCase();
  if (load.completed) return 'Completed';
  if (s === 'unassigned') return 'Unassigned';
  if (s === 'planned') return 'Planned';
  const inTransit =
    s === 'in_transit' ||
    (load.stops ?? []).some((x) => x.pickedUpAt || x.manualCompletedAt);
  return inTransit ? 'In transit' : 'Awaiting pickup';
}

export const orderedStops = (load) =>
  [...(load.stops ?? [])].sort((a, b) => a.sequence - b.sequence);

// DispatchBoardRow.Origin / Destination
export const origin = (load) =>
  orderedStops(load).find((x) => !x.driverOnly);
export const destination = (load) => orderedStops(load).at(-1);

// DispatchBoardRow.Location
export function location(stop) {
  if (!stop) return 'Pending';
  const parts = [stop.city, stop.province].filter((x) => x && x.trim());
  return parts.length ? parts.join(', ') : stop.address || '—';
}

export const text = (value, fallback) =>
  value && String(value).trim()
    ? value
    : fallback && String(fallback).trim()
      ? fallback
      : '—';

// Pages/Dispatch/DispatchRigStatus.cs
export function rigStatus(speed, engineState, hos, now = Date.now()) {
  if ((speed ?? 0) >= 1) return 'moving';
  const engine = (engineState ?? '').trim().toLowerCase();
  if (['idle', 'idling', 'on', 'running'].includes(engine)) return 'idling';
  if (engine !== 'off') return 'parked';
  const updated = hos?.updatedAt ? Date.parse(hos.updatedAt) : 0;
  return hos?.currentDutyStatus === 'sleeperBerth' &&
    updated > now - 15 * 60000
    ? 'sleeping'
    : 'off';
}

// DispatchList.razor.cs TruckMotionLabel
export function motionLabel(status, speed) {
  switch (status) {
    case 'moving':
      return `Driving · ${Math.round(speed ?? 0)} mph`;
    case 'sleeping':
      return 'Sleeper Berth';
    case 'idling':
      return 'Idle';
    case 'off':
      return 'Engine off';
    default:
      return 'Parked';
  }
}

// Shared/DriverStatus/DriverDutySummary Label
export const dutyLabel = (value) =>
  ({
    driving: 'Driving',
    onDuty: 'On Duty',
    yardMove: 'Yard Move',
    offDuty: 'Off Duty',
    sleeperBerth: 'Sleeper Berth',
    personalConveyance: 'Personal Conveyance (PC)',
  })[value] ?? '—';

// Shared/DriverStatus/DriverHours: a clock at or under one hour is low.
export const hosClocks = (hos) => [
  ['Break', hos?.breakMs, 8],
  ['Drive', hos?.driveMs, 11],
  ['Shift', hos?.shiftMs, 14],
  ['Cycle', hos?.cycleMs, 70],
];
export const isLowClock = (ms) => ms !== null && ms !== undefined &&
  ms <= 3600000;

export function formatClock(ms) {
  if (ms === null || ms === undefined) return '—';
  const m = Math.max(0, Math.floor(ms / 60000));
  return `${Math.floor(m / 60)}:${String(m % 60).padStart(2, '0')}`;
}

// StopAppointmentDisplay: date, time and optional window end, as sent.
export function appointment(stop, fallbackDate) {
  const date = stop?.scheduledDate ?? fallbackDate;
  if (!date) return 'Date pending';
  const d = dateOnly(date);
  const day = d.toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
  });
  const t1 = shortTime(stop?.scheduledTime);
  let out = t1 ? `${day}, ${t1}` : day;
  if (stop?.isWindow && (stop.scheduledDate2 || stop.scheduledTime2)) {
    const t2 = shortTime(stop.scheduledTime2);
    const d2 = stop.scheduledDate2 && stop.scheduledDate2 !== date
      ? dateOnly(stop.scheduledDate2).toLocaleDateString('en-US', {
          month: 'short',
          day: 'numeric',
        }) + ' '
      : '';
    if (t2 || d2) out += ` – ${d2}${t2}`;
  }
  return out;
}

export function shortTime(t) {
  if (!t) return '';
  const [h, m] = String(t).split(':').map(Number);
  if (Number.isNaN(h)) return '';
  const ap = h >= 12 ? 'PM' : 'AM';
  return `${((h + 11) % 12) + 1}:${String(m || 0).padStart(2, '0')} ${ap}`;
}

export function stopJob(job) {
  const j = (job ?? '').toLowerCase().replace(/[\s_-]/g, '');
  if (j.startsWith('pick')) return 'Pickup';
  if (j.startsWith('deliver') || j === 'dropoff') return 'Delivery';
  if (j.startsWith('hook')) return 'Hook trailer';
  if (j.startsWith('drop')) return 'Drop trailer';
  if (j.startsWith('switch')) return 'Switch';
  return 'Stop';
}

// DispatchBoardRow.StopCompleted
export const stopDone = (stop) => stop?.isCompleted === true;

export function formatInstant(iso, timeZone) {
  if (!iso) return '—';
  const d = new Date(iso);
  const opts = {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  };
  try {
    return d.toLocaleString('en-US', timeZone ? { ...opts, timeZone } : opts);
  } catch {
    return d.toLocaleString('en-US', opts);
  }
}

export function ago(ms) {
  const s = Math.round(ms / 1000);
  if (s < 60) return `${s}s ago`;
  const m = Math.round(s / 60);
  if (m < 60) return `${m} min ago`;
  return `${Math.round(m / 60)} h ago`;
}
