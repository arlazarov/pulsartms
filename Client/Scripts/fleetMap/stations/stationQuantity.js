// @ts-check
/** @param {number} gallons @param {{country?: string | null} | null | undefined} station @param {string} [unit] @param {(value: number) => number} [rounding] */
export function stationQuantity(gallons, station, unit = '', rounding = Math.ceil) {
  if (!Number.isFinite(gallons) || gallons < 0) return '';
  const country = (station?.country || '').trim().toUpperCase();
  const normalized = unit.trim().toLowerCase();
  const liters = ['l', 'liter', 'litre', 'liters', 'litres'].includes(normalized);
  const usGallons = ['gal', 'gallon', 'gallons', 'us gal', 'us gallon'].includes(normalized);
  const canada = liters || !usGallons && ['CA', 'CAN', 'CANADA'].includes(country);
  return canada ? `${rounding(gallons * 3.785411784)} L` : `${rounding(gallons)} US gal`;
}

/** @param {{gallons?: number | null, unit?: string, full?: boolean} | null | undefined} fuel @param {{country?: string | null} | null | undefined} station */
export function stationPurchase(fuel, station) {
  if (typeof fuel?.gallons !== 'number' || !Number.isFinite(fuel.gallons)) return '';
  const quantity = stationQuantity(fuel.gallons, station, fuel.unit);
  return fuel.full ? 'Fill up' : `Buy ${quantity}`;
}
