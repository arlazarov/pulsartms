import { fuelVisitLabel } from './stationQuantity.ts';

// One planned visit to a station, and - when the plan stops there more than
// once - the visits it stands for.
export type PlannedFuel = {
  id?: string;
  number?: number;
  numbers?: string;
  routeMile?: number;
  miles?: number;
  visits?: PlannedFuel[];
  // Where the station is and what it sells, for a station the map knows
  // only because the plan stops there.
  name?: string;
  address?: string;
  currency?: string;
  unit?: string;
  yourPrice?: number;
  point?: { latitude?: number; longitude?: number };
  [key: string]: unknown;
};

// What the map is told to redraw after the truck has driven on: the card
// shows this fuel (nothing, once the visit is behind it), and the mark is
// only redrawn when what it says has changed.
export type PlanChange = {
  id: string;
  fuel: PlannedFuel | null;
  mark: boolean;
};

/**
 * The fuel plan as the map holds it: which stations it recommends, what each
 * planned visit buys there, and how that changes as the truck drives past
 * them. A station the truck has passed leaves the plan - it is no longer
 * somewhere the driver can stop.
 */
export function createStationPlan() {
  let recommended = new Set<string>();
  let quantities = new Map<string, PlannedFuel>();

  return {
    recommends: (id: string | null) => id !== null && recommended.has(id),
    fuelAt: (id: string | null) =>
      id === null ? undefined : quantities.get(id),
    stations: () => quantities,
    // The page sends either bare station ids or a planned visit each.
    set(ids: (string | PlannedFuel)[]) {
      recommended = new Set(
        ids.map(x => (typeof x === 'string' ? x : (x.id ?? ''))),
      );
      quantities = new Map(
        ids
          .filter((x): x is PlannedFuel => typeof x !== 'string')
          .map(x => [x.id ?? '', x]),
      );
    },
    // The truck has reached this mile of its route.
    advance(miles: number): PlanChange[] {
      const changes: PlanChange[] = [];
      for (const [id, quantity] of quantities) {
        if (quantity.visits) {
          const visits = quantity.visits
            .filter(
              visit =>
                !Number.isFinite(visit.routeMile) ||
                visit.routeMile! >= miles - 0.5,
            )
            .map(visit =>
              Number.isFinite(visit.routeMile)
                ? { ...visit, miles: Math.max(0, visit.routeMile! - miles) }
                : visit,
            );
          const next = visits.length
            ? {
                ...visits[0],
                visits,
                numbers: visits.map(visit => visit.number).join('/'),
              }
            : null;
          if (next) quantities.set(id, next);
          else {
            quantities.delete(id);
            recommended.delete(id);
          }
          changes.push({
            id,
            fuel: next,
            mark: fuelVisitLabel(quantity) !== fuelVisitLabel(next),
          });
          continue;
        }
        if (!Number.isFinite(quantity.routeMile)) continue;
        if (quantity.routeMile! < miles - 0.5) {
          quantities.delete(id);
          recommended.delete(id);
          changes.push({ id, fuel: null, mark: true });
          continue;
        }
        const ahead = Math.max(0, quantity.routeMile! - miles);
        // Neither the miles nor the kilometres the card shows would read any
        // differently, so nothing is redrawn.
        if (
          Math.round(ahead) === Math.round(quantity.miles!) &&
          Math.round(ahead * 1.609344) ===
            Math.round(quantity.miles! * 1.609344)
        )
          continue;
        quantity.miles = ahead;
        changes.push({ id, fuel: quantity, mark: false });
      }
      return changes;
    },
    clear() {
      quantities.clear();
      recommended.clear();
    },
  };
}
