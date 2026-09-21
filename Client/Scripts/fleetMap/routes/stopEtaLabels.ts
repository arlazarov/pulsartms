import type { DispatchEta } from '../contracts.d.ts';
import type { HoursRow } from './stopHoursLabels.ts';
import { cycleStatus, stopHoursLabels } from './stopHoursLabels.ts';

// What a card says about one stop's arrival: the hour, whether it is late,
// and the cycle behind it.
export type StopEtaLabel = {
  text: string;
  arrivalText: string;
  statusText: string;
  arrivalStatusText?: string;
  cycleStatusText?: string;
  tone: 'eta' | 'success' | 'danger';
  etaLabel?: string;
  hours?: HoursRow[];
};

const PENDING_DISPLAY_GRACE_MS = 15 * 60_000;

export function stopEtaDeadline(eta: DispatchEta | null | undefined): number {
  return (
    Date.parse(eta?.validUntil ?? '') +
    (eta?.routeUpdatePending ? PENDING_DISPLAY_GRACE_MS : 0)
  );
}

export function stopEtaLabels(
  eta: DispatchEta | null | undefined,
  now = Date.now(),
): Map<string, StopEtaLabel> {
  const labels = new Map<string, StopEtaLabel>();
  if (!eta || !(stopEtaDeadline(eta) > now)) return labels;
  for (const stop of eta.stops || []) {
    if (
      !stop.stopId ||
      !stop.dispatchId ||
      !stop.timeZoneId ||
      !Number.isFinite(Date.parse(stop.arrival))
    )
      continue;
    try {
      const local = new Intl.DateTimeFormat('en-US', {
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        hourCycle: 'h12',
        timeZone: stop.timeZoneId,
      }).format(new Date(stop.arrival));
      const hasStatus =
        Number.isFinite(Date.parse(stop.appointment ?? '')) &&
        typeof stop.lateMinutes === 'number' &&
        Number.isFinite(stop.lateMinutes) &&
        stop.lateMinutes >= 0;
      const cycle = cycleStatus(stop);
      const known = hasStatus && !cycle;
      const late = hasStatus && (stop.lateMinutes ?? 0) > 0;
      const hours = stopHoursLabels(stop, eta, now);
      const etaLabel = 'ETA';
      const arrivalText = hours ? local.replace(',', ' ·') : local;
      labels.set(`${stop.dispatchId}:${stop.stopId}`, {
        text: `${etaLabel} ${arrivalText} local${late ? ' · Late' : ''}`,
        arrivalText,
        statusText: cycle
          ? `${late ? 'Late · ' : ''}${cycle}`
          : known
            ? late
              ? 'Late'
              : 'On time'
            : '',
        ...(cycle
          ? { arrivalStatusText: late ? 'Late' : '', cycleStatusText: cycle }
          : {}),
        tone:
          late || cycle === 'Cycle short'
            ? 'danger'
            : known
              ? 'success'
              : 'eta',
        ...(hours ? { etaLabel, hours } : {}),
      });
    } catch {}
  }
  return labels;
}
