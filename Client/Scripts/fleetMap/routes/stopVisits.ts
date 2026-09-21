import type { PlanStop } from '../contracts.d.ts';
import { addressLines } from '../ui/addressLines.ts';

// Which visit of which address a stop is, when a route calls at the same
// place more than once.
export type StopVisit = {
  number: number;
  count: number;
  visitNumber: number;
  visitCount: number;
};

export function stopVisits(stops: PlanStop[]): Map<string, StopVisit> {
  const addresses = new Map<string, string[]>();
  for (const stop of stops) {
    const address = addressKey(stop.address);
    if (!address) continue;
    if (!addresses.has(address)) addresses.set(address, []);
    addresses.get(address)!.push(stop.id);
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

function addressKey(value: unknown): string {
  if (typeof value !== 'string') return '';
  const address = addressLines(value);
  return address.street && address.locality
    ? value.trim().replace(/\s+/g, ' ').toUpperCase()
    : '';
}
