// @ts-check
import { futureRouteColor } from '../rendering/routePalette.js';

/** @typedef {{loadId: string | undefined, executionLegId?: string | null, loadNumber: number, index: number}} StopSelection */
/** @typedef {{stop: import('../contracts.d.ts').NextLoadStop, numbers: Set<number>, members: StopSelection[], color: import('../rendering/routePalette.js').RouteColor}} StopGroup */

/** @param {import('../contracts.d.ts').NextLoad[]} loads */
export function nextLoadDisplay(loads) {
  /** @type {{points: import('../contracts.d.ts').RoutePoint[], role: 'deadhead' | 'future', loadId: string | number, routeColor?: import('../rendering/routePalette.js').RouteColor, routeDepth?: number}[]} */
  const lines = [];
  /** @type {StopGroup[]} */
  const groups = [];
  let stopNumber = 0;
  for (const [loadIndex, load] of loads.entries()) {
    const points = load.deadhead?.points || [];
    const loadId = nextLoadKey(load.id ?? load.loadNumber, load.executionLegId);
    const color = futureRouteColor(loadIndex);
    // How far down the chain a load sits is drawn, not only coloured: the
    // second load after this one matters less than the first.
    if (points.length > 1)
      lines.push({ points, role: 'deadhead', loadId, routeDepth: loadIndex });
    for (const leg of load.legs || []) {
      if (leg.points.length > 1)
        lines.push({
          points: leg.points,
          role: 'future',
          loadId,
          routeColor: color,
          routeDepth: loadIndex,
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
  return { lines, groups };
}

/** @param {string | number} id
 * @param {string | null | undefined} executionLegId */
export function nextLoadKey(id, executionLegId) {
  return executionLegId ? `${id}:${executionLegId}` : id;
}
