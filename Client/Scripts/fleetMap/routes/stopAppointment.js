const dayFormat = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  day: 'numeric',
  timeZone: 'UTC',
});
const yearFormat = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  day: 'numeric',
  year: 'numeric',
  timeZone: 'UTC',
});

function calendarDate(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value))
    return null;
  const date = new Date(`${value}T00:00:00Z`);
  return Number.isFinite(date.getTime()) &&
    date.toISOString().slice(0, 10) === value
    ? date
    : null;
}

function clock(value) {
  if (
    typeof value !== 'string' ||
    !/^([01]\d|2[0-3]):[0-5]\d(?::[0-5]\d(?:\.\d+)?)?$/.test(value)
  )
    return '';
  const hour = Number(value.slice(0, 2));
  return `${String(hour % 12 || 12).padStart(2, '0')}:${value.slice(3, 5)} ${hour < 12 ? 'AM' : 'PM'}`;
}

export function stopAppointment(stop) {
  const start = calendarDate(stop.scheduledDate);
  const end = calendarDate(
    stop.scheduledDate2 ?? (stop.scheduledTime2 ? stop.scheduledDate : null),
  );
  const format =
    start && end && start.getUTCFullYear() !== end.getUTCFullYear()
      ? yearFormat
      : dayFormat;
  const firstTime = clock(stop.scheduledTime),
    lastTime = clock(stop.scheduledTime2);
  const sameDay = start?.getTime() === end?.getTime();
  const first = [start ? format.format(start) : '', firstTime]
    .filter(Boolean)
    .join(' · ');
  const last = [
    end && !sameDay ? format.format(end) : '',
    sameDay && firstTime === lastTime ? '' : lastTime,
  ]
    .filter(Boolean)
    .join(' · ');
  return (
    [first, last && last !== first ? last : ''].filter(Boolean).join(' – ') ||
    '—'
  );
}
