// Views used by both Fleet and Dispatch: the trip chain, trip cards with
// stops, HOS clocks and the facts grid. One implementation, two screens.
import { esc, icon, phaseBadge } from './ui.js';
import { etaFor } from './model.js';
import {
  appointment,
  formatClock,
  formatInstant,
  hosClocks,
  isLowClock,
  phaseLabel,
  stopDone,
  stopJob,
  location,
  tripStopLabels,
} from './rules.js';
import { state } from './store.js';

export function chain(t, selectedId) {
  if (!t)
    return `<div class="chain-label"><span class="eyebrow">Trip chain</span>
      <b>No truck selected</b></div>
      <div class="chain"><span class="muted">Choose a truck on the map, in
      the list or with search to see its trips in order.</span></div>`;
  const links = t.trips
    .map(
      (x, i) => `
      <li class="link ${x.phase}" data-selected="${x.id === selectedId}">
        ${i ? `<span class="arrow">${icon('right', 'sm')}</span>` : ''}
        <span class="num-bullet ${x.phase}">${x.index}</span>
        <button class="link-card" data-action="trip" data-id="${x.id}"
          data-key="chain-${x.id}" aria-pressed="${x.id === selectedId}">
          <span class="load">${esc(x.loadNumber)}</span>
          ${phaseBadge(x.phase, phaseLabel[x.phase])}
          <span class="lane">${esc(x.fromText)} → ${esc(x.toText)}</span>
        </button>
      </li>`
    )
    .join('');
  return `<div class="chain-label"><span class="eyebrow">Trip chain</span>
      <b>Truck ${esc(t.unit)}</b>
      <span class="muted">${t.trips.length} trip${
        t.trips.length === 1 ? '' : 's'} on the board</span></div>
    <ol class="chain" data-scroll="chain" aria-label="Trips of truck ${esc(
      t.unit)} in order" style="list-style:none;margin:0">
      ${links || '<li class="muted">No active or planned trips.</li>'}
    </ol>
    <div class="legend desktop-only" aria-hidden="true">
      <i class="current"></i><span>Current trip</span>
      <i class="next"></i><span>Next trip</span>
      <i class="upcoming"></i><span>Upcoming trip</span>
      <i class="other"></i><span>Other trucks</span>
    </div>`;
}

export function hos(t) {
  return `<div class="hos" role="group" aria-label="Hours of service">
    ${hosClocks(t.hos)
      .map(([name, ms, full]) => {
        const pct = ms == null ? 0 : Math.min(100, (ms / (full * 3600000)) *
          100);
        return `<div class="hos-clock ${isLowClock(ms) ? 'low' : ''}">
          <div class="k">${name}</div>
          <div class="v">${formatClock(ms)}</div>
          <div class="bar"><i style="width:${pct.toFixed(0)}%"></i></div>
        </div>`;
      })
      .join('')}
  </div>`;
}

export function stopList(t, x) {
  const sel = state.selection.stopId;
  const firstOpen = x.stops.find((s) => !stopDone(s));
  const labels = tripStopLabels(x.stops.map((s) => s.job));
  return `<ol class="stops">${x.stops
    .map((s, i) => {
      const done = stopDone(s);
      const eta = etaFor(t, s.id);
      const markCls = done ? 'done' : s === firstOpen ? 'next' : '';
      return `<li><button class="stop-row" data-action="stop"
        data-trip="${x.id}" data-id="${s.id}" data-key="stop-${s.id}"
        aria-pressed="${s.id === sel}">
        <span class="mark ${markCls}" aria-label="${esc(labels[i])}">${
          done ? icon('check', 'sm') : esc(labels[i])}</span>
        <span class="job">${stopJob(s.job)}${
          done ? ' <span class="muted">· Completed</span>' : s === firstOpen
            ? ' <span class="muted">· Next stop</span>' : ''}</span>
        <span class="place">${esc(s.name ?? '')}${s.name ? ' · ' : ''}${esc(
          location(s))}</span>
        <span class="time">${esc(appointment(s))}<small>${
          eta?.arrival && !done
            ? `ETA ${esc(formatInstant(eta.arrival, eta.timeZoneId))}${
              eta.lateMinutes > 0 ? ` · <b style="color:var(--danger)">late ${
                eta.lateMinutes} min</b>` : ''}`
            : 'Appointment'}</small></span>
      </button></li>`;
    })
    .join('')}</ol>`;
}

export function tripCard(t, x, open) {
  const selected = state.selection.dispatchId
    ? state.selection.dispatchId === x.id
    : x.phase === 'current';
  const last = x.to;
  const progress = t.summary?.dispatchId === x.id
    ? t.summary?.state?.progress : null;
  return `<li class="trip" data-open="${open}" data-selected="${selected}">
    <span class="num-bullet ${x.phase}">${x.index}</span>
    <div class="trip-card">
      <button class="trip-summary" data-action="expand" data-id="${x.id}"
        data-key="trip-${x.id}" aria-expanded="${open}">
        <span class="line1">${phaseBadge(x.phase, phaseLabel[x.phase])}
          <span class="load">${esc(x.loadNumber)}</span></span>
        <span class="when">${icon('chevron', 'chev')}</span>
        <span class="lane">${esc(x.fromText)} ${icon('right', 'sm')} ${esc(
          x.toText)}</span>
        <span class="meta"><span>${esc(x.status)}</span><b>${esc(
          appointment(last))}</b></span>
      </button>
      ${open ? `<div class="trip-body">
        ${stopList(t, x)}
        <dl class="kv">
          ${progress ? `<dt>Remaining</dt><dd>${Math.round(
            progress.remainingMiles)} mi</dd>` : ''}
          <dt>Order</dt><dd>${esc(x.load.orderNumber ?? '—')}</dd>
          <dt>Customer</dt><dd>${esc(x.load.customerName ?? '—')}</dd>
          ${x.load.loadedMiles ? `<dt>Loaded miles</dt><dd>${esc(
            x.load.loadedMiles)} mi</dd>` : ''}
        </dl>
      </div>` : ''}
    </div>
  </li>`;
}

export function attentionList(t) {
  if (!t.attention.length) return '';
  return `<div style="display:grid;gap:6px;margin:0 var(--sp-lg) var(--sp-md)">
    ${t.attention
      .map(
        (a) => `<div class="alert ${a.level === 'danger' ? 'danger' : ''}">
        ${icon('alert', 'sm')}<span>${esc(a.text)}${a.detail
          ? `<span class="sub">${esc(a.detail)}</span>` : ''}</span></div>`
      )
      .join('')}</div>`;
}
