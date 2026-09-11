// @ts-check

/** @param {unknown} value */
const finite = value => typeof value === 'number' && Number.isFinite(value) ? value : null;
/** @param {unknown} value */
const identity = value => typeof value === 'string' && value.length > 0
  && value !== '00000000-0000-0000-0000-000000000000' ? value : null;

/** @param {import('../contracts.d.ts').RouteMetadata | null} plan
 * @param {import('../contracts.d.ts').RouteProgress | null} progress
 * @param {boolean} [showNextLoads] */
export function fuelRecommendations(plan, progress, showNextLoads = true) {
  if (!plan?.fuelPlan || plan.fuelPlan.needsRefresh) return { key: '', stops: [] };
  const current = finite(progress?.progressMiles);
  const dispatchId = identity(plan.dispatchId);
  const currentStopIds = showNextLoads ? null : new Set([...(plan.referenceStops ?? []), ...(plan.stops ?? [])]
    .map(stop => identity(stop.id)).filter(id => id !== null));
  const visits = plan.fuelPlan.stops.flatMap((stop, index) => {
    if (!showNextLoads) {
      const owner = identity(stop.dispatchId), beforeStopId = identity(stop.beforeStopId);
      // Explicit ownership takes precedence; legacy plans need a known current stop.
      if (owner ? owner !== dispatchId : !beforeStopId || !currentStopIds?.has(beforeStopId)) return [];
    }
    const routeMile = finite(stop.currentRouteMile), serverMiles = finite(stop.milesAhead);
    if (!stop.stationId || !Number.isFinite(stop.buyGallons) || stop.buyGallons <= 0
      || routeMile !== null && current !== null && routeMile < current - .5
      || routeMile === null && serverMiles !== null && serverMiles < 0) return [];
    return [{ id: stop.stationId, number: typeof stop.number === 'number' && Number.isInteger(stop.number) && stop.number > 0 ? stop.number : index + 1,
      visitKey: stop.visitKey ?? '', beforeStopId: stop.beforeStopId ?? null, gallons: stop.buyGallons,
      arrivalGallons: finite(stop.arrivalGallons), departureGallons: finite(stop.departureGallons),
      purchaseCostUsd: finite(stop.purchaseCostUsd),
      point: stop.point, name: stop.name, address: stop.address,
      yourPrice: finite(stop.yourPrice), currency: stop.currency,
      tankGallons: finite(plan.tankGallons), unit: stop.unit ?? '',
      full: stop.fillToTarget, routeMile,
      miles: current !== null && routeMile !== null ? Math.max(0, routeMile - current) : serverMiles }];
  });
  visits.sort((a, b) => (a.miles ?? Infinity) - (b.miles ?? Infinity));
  /** @type {Map<string, typeof visits[number] & { visits: typeof visits }>} */
  const stations = new Map();
  for (const visit of visits) {
    const entry = stations.get(visit.id);
    if (entry) entry.visits.push(visit);
    else stations.set(visit.id, { ...visit, visits: [visit] });
  }
  const stops = [...stations.values()].map(stop => ({ ...stop,
    numbers: stop.visits.map(visit => visit.number).join('/') }));
  return { key: JSON.stringify(stops), stops };
}
