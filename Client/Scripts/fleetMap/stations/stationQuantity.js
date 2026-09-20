export function stationQuantity(
  gallons,
  station,
  unit = '',
  rounding = Math.ceil,
) {
  if (!Number.isFinite(gallons) || gallons < 0) return '';
  const country = (station?.country || '').trim().toUpperCase();
  const normalized = unit.trim().toLowerCase();
  const liters = ['l', 'liter', 'litre', 'liters', 'litres'].includes(
    normalized,
  );
  const usGallons = [
    'gal',
    'gallon',
    'gallons',
    'us gal',
    'us gallon',
  ].includes(normalized);
  const canada =
    liters || (!usGallons && ['CA', 'CAN', 'CANADA'].includes(country));
  return canada
    ? `${rounding(gallons * 3.785411784)} L`
    : `${rounding(gallons)} US gal`;
}

// What the badge over a planned stop says. The stop number alone told the
// dispatcher where in the order it falls but not what it is for; how much
// is bought there is the reason the stop exists.
export function fuelVisitLabel(fuel, station) {
  const numbers = typeof fuel?.numbers === 'string' ? fuel.numbers.trim() : '';
  // Nothing planned here carries no badge, exactly as before.
  if (!numbers) return undefined;
  const quantity = stationQuantity(fuel.gallons, station, fuel.unit, Math.round)
    .replace(' US gal', ' gal')
    .trim();
  return quantity ? `${numbers} · ${quantity}` : numbers;
}

export function stationPurchase(fuel, station) {
  if (!Number.isFinite(fuel?.gallons)) return '';
  const quantity = stationQuantity(fuel.gallons, station, fuel.unit);
  return fuel.full ? 'Fill up' : `Buy ${quantity}`;
}
