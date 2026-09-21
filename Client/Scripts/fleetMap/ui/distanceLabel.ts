// What is left to drive, in the unit the account reads in.
export function distanceLabel(miles: number, unit = 'both'): string {
  const valid = Number.isFinite(miles) && miles >= 0;
  if (!valid) return '—';
  const mi = Math.round(miles).toLocaleString('en-US');
  const km = Math.round(miles * 1.609344).toLocaleString('en-US');
  if (unit === 'miles') return `${mi} mi`;
  if (unit === 'kilometers') return `${km} km`;
  return `${mi} mi · ${km} km`;
}
