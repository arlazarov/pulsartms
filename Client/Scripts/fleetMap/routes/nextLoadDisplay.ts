// @ts-check
import { futureRouteColor } from '../rendering/routePalette.ts';
import { markSharedRoads } from './sharedRoads.ts';

/** @typedef {{loadId: string | undefined, executionLegId?: string | null, loadNumber: number, index: number}} StopSelection */
/** @typedef {{stop: import('../contracts.d.ts').NextLoadStop, numbers: Set<number>, members: StopSelection[], color: import('../rendering/routePalette.ts').RouteColor}} StopGroup */

/** @param {import('../contracts.d.ts').NextLoad[]} loads */
export function nextLoadDisplay(loads: any[]) {
  /** @type {{points: import('../contracts.d.ts').RoutePoint[], role: 'deadhead' | 'future', loadId: string | number, routeColor?: import('../rendering/routePalette.ts').RouteColor}[]} */
  const lines = [];
  /** @type {StopGroup[]} */
  const groups = [];
  let stopNumber = 0;
  for (const [loadIndex, load] of loads.entries()) {
    const points = load.deadhead?.points || [];
    const loadId = nextLoadKey(load.id ?? load.loadNumber, load.executionLegId);
    const color = futureRouteColor(loadIndex);
    if (points.length > 1) lines.push({ points, role: 'deadhead', loadId });
    for (const leg of load.legs || []) {
      if (leg.points.length > 1)
        lines.push({
          points: leg.points,
          role: 'future',
          loadId,
          routeColor: color,
        });
    }
    const lastPoint = points.at(-1);
    const stops = load.stops.length
      ? load.stops
      : points.length > 1 && lastPoint
        ? [{ ...lastPoint, job: 'Pickup' }]
        : [];
    for (const [index, stop] of stops.entries()) {
      const number = ++stopNumber;
      groups.push({
        stop,
        numbers: new Set([number]),
        members: [
          {
            loadId: load.id,
            ...(load.executionLegId
              ? { executionLegId: load.executionLegId }
              : {}),
            loadNumber: load.loadNumber,
            index,
          },
        ],
        color,
      });
    }
    stopNumber += Math.max(
      0,
      (load.stopCount ?? load.stops.length) - stops.length,
    );
  }
  return { lines: markSharedRoads(lines), groups };
}

// A load and the leg of it being driven, as one name.
export function nextLoadKey(
  id: string | number | undefined,
  executionLegId: string | null | undefined,
): string {
  return executionLegId ? `${id}:${executionLegId}` : String(id ?? '');
}
