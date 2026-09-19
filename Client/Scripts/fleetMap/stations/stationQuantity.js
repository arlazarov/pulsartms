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

export function stationPurchase(fuel, station) {
  if (!Number.isFinite(fuel?.gallons)) return '';
  const quantity = stationQuantity(fuel.gallons, station, fuel.unit);
  return fuel.full ? 'Fill up' : `Buy ${quantity}`;
}
