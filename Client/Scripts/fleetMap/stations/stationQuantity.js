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

// What the badge over a planned stop says: which stop of the fuel plan it
// is, and nothing else. How much is bought there rode along for a while and
// made the badge a sentence across the map; the card says it, in a place
// where there is room to say it properly.
export function fuelVisitLabel(fuel) {
  const numbers = typeof fuel?.numbers === 'string' ? fuel.numbers.trim() : '';
  // Nothing planned here carries no badge, exactly as before.
  return numbers || undefined;
}

export function stationPurchase(fuel, station) {
  if (!Number.isFinite(fuel?.gallons)) return '';
  const quantity = stationQuantity(fuel.gallons, station, fuel.unit);
  return fuel.full ? 'Fill up' : `Buy ${quantity}`;
}
