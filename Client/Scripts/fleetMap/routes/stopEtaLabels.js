// @ts-check

import { cycleStatus, stopHoursLabels } from './stopHoursLabels.js';

const PENDING_DISPLAY_GRACE_MS = 15 * 60_000;

/** @param {import('../contracts.d.ts').DispatchEta | null | undefined} eta */
export function stopEtaDeadline(eta) {
  return Date.parse(eta?.validUntil ?? '') + (eta?.routeUpdatePending ? PENDING_DISPLAY_GRACE_MS : 0);
}

/** @param {import('../contracts.d.ts').DispatchEta | null | undefined} eta @param {number} [now] */
export function stopEtaLabels(eta, now = Date.now()) {
  /** @type {Map<string, {text: string, arrivalText: string, statusText: string, arrivalStatusText?: string, cycleStatusText?: string, tone: 'eta'|'success'|'danger', etaLabel?: string, hours?: import('./stopHoursLabels.js').HoursRow[]}>} */
  const labels = new Map();
  if (!eta || !(stopEtaDeadline(eta) > now)) return labels;
  for (const stop of eta.stops || []) {
    if (!stop.stopId || !stop.dispatchId || !stop.timeZoneId || !Number.isFinite(Date.parse(stop.arrival))) continue;
    try {
      const local = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', hour: '2-digit',
        minute: '2-digit', hourCycle: 'h12', timeZone: stop.timeZoneId }).format(new Date(stop.arrival));
      const hasStatus = Number.isFinite(Date.parse(stop.appointment ?? '')) && typeof stop.lateMinutes === 'number'
        && Number.isFinite(stop.lateMinutes) && stop.lateMinutes >= 0;
      const cycle = cycleStatus(stop);
      const known = hasStatus && !cycle;
      const late = hasStatus && (stop.lateMinutes ?? 0) > 0;
      const hours = stopHoursLabels(stop, eta, now);
      const etaLabel = 'ETA';
      const arrivalText = hours ? local.replace(',', ' ·') : local;
      labels.set(`${stop.dispatchId}:${stop.stopId}`, {
        text: `${etaLabel} ${arrivalText} local${late ? ' · Late' : ''}`,
        arrivalText,
        statusText: cycle ? `${late ? 'Late · ' : ''}${cycle}` : known ? late ? 'Late' : 'On time' : '',
        ...(cycle ? { arrivalStatusText: late ? 'Late' : '', cycleStatusText: cycle } : {}),
        tone: late || cycle === 'Cycle short' ? 'danger' : known ? 'success' : 'eta',
        ...(hours ? { etaLabel, hours } : {}),
      });
    } catch { }
  }
  return labels;
}
