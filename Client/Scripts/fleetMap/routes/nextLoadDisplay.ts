import type { NextLoad, NextLoadStop, RoutePoint } from '../contracts.d.ts';
import type { RouteColor } from '../rendering/routePalette.ts';
import { futureRouteColor } from '../rendering/routePalette.ts';
import { stopBadge } from './stopBadges.ts';

// Which load a badge stands for, and which of its stops the card opens on.
// A stop on a leg already being driven names that leg as well.
export type StopSelection = {
  loadId: string | undefined;
  executionLegId?: string | null;
  loadNumber: number;
  index: number;
};

// One badge for one stop of one load: its label within that load, and the
// number that orders it among every badge drawn.
export type StopGroup = {
  stop: NextLoadStop;
  numbers: Set<number>;
  labels: string[];
  members: StopSelection[];
  color: RouteColor;
};

// One stretch of road drawn for an upcoming load: the empty miles to its
// pickup, or a leg of the load itself.
export type DisplayLine = {
  points: RoutePoint[];
  role: 'deadhead' | 'future';
  loadId: string;
  routeColor?: RouteColor;
  // The load's place in the chain: the next load is drawn over the ones
  // after it where they run on one road.
  chain: number;
};

export function nextLoadDisplay(loads: NextLoad[]) {
  const lines: DisplayLine[] = [];
  const groups: StopGroup[] = [];
  let stopNumber = 0;
  for (const [loadIndex, load] of loads.entries()) {
    const points = load.deadhead?.points || [];
    const loadId = nextLoadKey(load.id ?? load.loadNumber, load.executionLegId);
    const color = futureRouteColor(loadIndex);
    if (points.length > 1)
      lines.push({ points, role: 'deadhead', loadId, chain: loadIndex });
    for (const leg of load.legs || []) {
      if (leg.points.length > 1)
        lines.push({
          points: leg.points,
          role: 'future',
          loadId,
          routeColor: color,
          chain: loadIndex,
        });
    }
    const lastPoint = points.at(-1);
    const stops = load.stops.length
      ? load.stops
      : points.length > 1 && lastPoint
        ? [{ ...lastPoint, job: 'Pickup' }]
        : [];
    const labels = stops.map(stop => stopBadge(stop.id, stop.job));
    for (const [index, stop] of stops.entries()) {
      const number = ++stopNumber;
      groups.push({
        stop,
        numbers: new Set([number]),
        labels: [labels[index]],
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

// A load and the leg of it being driven, as one name.
export function nextLoadKey(
  id: string | number | undefined,
  executionLegId: string | null | undefined,
): string {
  return executionLegId ? `${id}:${executionLegId}` : String(id ?? '');
}
