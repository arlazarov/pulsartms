// @ts-check

/** @typedef {{label: string, value: string, title?: string, recap?: boolean, credit?: string, status?: string, tone: 'neutral'|'danger'|'success'}} HoursRow */

/** @param {import('../contracts.d.ts').StopHoursAlternative} alternative */
export function alternativeTone(alternative) {
  const late = alternative.lateMinutes;
  return typeof late !== 'number' || !Number.isSafeInteger(late) || late < 0 ? 'neutral'
    : late === 0 ? 'success' : 'danger';
}

/** @param {number | null | undefined} minutes */
function signedDuration(minutes) {
  if (typeof minutes !== 'number' || !Number.isSafeInteger(minutes)) return '—';
  const value = Math.abs(minutes);
  return `${minutes < 0 ? '−' : minutes > 0 ? '+' : ''}${Math.floor(value / 60)}h ${String(value % 60).padStart(2, '0')}m`;
}

/** @param {string | null | undefined} date @param {string | null | undefined} zone @param {boolean} [dateOnly] */
function localTime(date, zone, dateOnly = false) {
  if (!date || !zone || !Number.isFinite(Date.parse(date))) return null;
  try {
    return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric',
      ...(dateOnly ? {} : { hour: '2-digit', minute: '2-digit', hourCycle: 'h12' }),
      timeZone: zone }).format(new Date(date)).replace(',', ' ·');
  } catch { return null; }
}

/** @param {import('../contracts.d.ts').StopEta} stop */
export function cycleStatus(stop) {
  if (!stop.hours) return null;
  if (!stop.hours.cycleVerified || typeof stop.hours.cycleAtArrivalMinutes !== 'number'
    || !Number.isSafeInteger(stop.hours.cycleAtArrivalMinutes)) return 'Cycle unknown';
  return Number.isFinite(Date.parse(stop.hours.firstCycleShortageAt ?? ''))
    || typeof stop.hours.drivingShortfallMinutes === 'number' && Number.isSafeInteger(stop.hours.drivingShortfallMinutes) && stop.hours.drivingShortfallMinutes > 0
    || stop.hours.cycleAtArrivalMinutes < 0 ? 'Cycle short' : null;
}

/** @param {import('../contracts.d.ts').StopEta} stop @param {import('../contracts.d.ts').DispatchEta} eta @param {number} now */
export function stopHoursLabels(stop, eta, now) {
  const hours = stop.hours;
  if (!hours) return null;
  const minutes = hours.cycleAtArrivalMinutes;
  /** @type {HoursRow[]} */
  const rows = [{ label: 'Cycle remaining', value: hours.cycleVerified ? signedDuration(minutes) : '—',
    title: 'Estimated cycle remaining on arrival',
    tone: hours.cycleVerified && typeof minutes === 'number' && Number.isSafeInteger(minutes) && minutes < 0 ? 'danger' : 'neutral' }];
  const baseline = eta.cycleAtCalculation;
  if (baseline?.recapVerified && typeof baseline.nextRecapMinutes === 'number' && Number.isSafeInteger(baseline.nextRecapMinutes) && baseline.nextRecapMinutes > 0
      && Date.parse(baseline.nextRecapAt ?? '') > now) {
    const when = localTime(baseline.nextRecapAt, baseline.homeTimeZoneId, true);
    if (when) rows.push({ label: 'Next recap', value: when, credit: signedDuration(baseline.nextRecapMinutes),
      recap: true, tone: 'neutral' });
  }
  if (hours.cycleVerified) {
    const seen = new Set();
    for (const alternative of hours.alternatives ?? []) {
      if (alternative.kind !== 'recap' || seen.has(alternative.kind)) continue;
      const when = localTime(alternative.arrival, stop.timeZoneId);
      const known = typeof alternative.lateMinutes === 'number' && Number.isSafeInteger(alternative.lateMinutes) && alternative.lateMinutes >= 0
        && Number.isFinite(Date.parse(stop.appointment ?? ''));
      if (!when || !known) continue;
      seen.add(alternative.kind);
      const onTime = alternative.lateMinutes === 0;
      const late = typeof alternative.lateMinutes === 'number' ? signedDuration(alternative.lateMinutes).replace(/^\+/, '') : '';
      rows.push({ label: 'With recap', value: when,
        status: onTime ? 'On time with recap' : `Late by ${late}`,
        tone: alternativeTone(alternative) });
    }
  }
  return rows;
}
