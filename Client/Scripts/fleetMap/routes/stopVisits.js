import { addressLines } from '../ui/addressLines.js';

export function stopVisits(stops) {
  const addresses = new Map();
  for (const stop of stops) {
    const address = addressKey(stop.address);
    if (!address) continue;
    if (!addresses.has(address)) addresses.set(address, []);
    addresses.get(address).push(stop.id);
  }
  return new Map(
    stops.map((stop, index) => {
      const visits = addresses.get(addressKey(stop.address)) ?? [];
      return [
        stop.id,
        {
          number: index + 1,
          count: stops.length,
          visitNumber: visits.indexOf(stop.id) + 1,
          visitCount: visits.length,
        },
      ];
    }),
  );
}

function addressKey(value) {
  if (typeof value !== 'string') return '';
  const address = addressLines(value);
  return address.street && address.locality
    ? value.trim().replace(/\s+/g, ' ').toUpperCase()
    : '';
}
