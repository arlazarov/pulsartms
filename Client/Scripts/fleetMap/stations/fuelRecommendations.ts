// @ts-check

/** @param {unknown} value */
const finite = (value: unknown) =>
  typeof value === 'number' && Number.isFinite(value) ? value : null;
/** @param {import('../contracts.d.ts').RouteMetadata | null} plan
 * @param {import('../contracts.d.ts').RouteProgress | null} progress
 */
// The stations the plan suggests, and how far along the road each one is.
export function fuelRecommendations(
  plan: any,
  progress: any,
): { key: string; stops: any[] } {
  const access = plan?.fuelRecommendations?.accessProblem
    ? plan.fuelRecommendations
    : null;
  const fuel = access
    ? {
        stops: access.stations.map((stop: any) => ({
          ...stop,
          warning:
            stop.shortfallGallons > 0
              ? `Cannot reach this station: ${stop.shortfallGallons.toFixed(1)} US gal short. Refuel before driving to it.`
              : 'This station is reachable, but no complete plan meets the remaining route and arrival requirements.',
          currentRouteMile: stop.routeMile,
          buyGallons: 0,
          accessOnly: true,
          dispatchId: plan?.dispatchId,
        })),
      }
    : plan?.fuelPlan;
  const refreshing =
    !access &&
    (progress?.offRoute === true ||
      (fuel?.needsRefresh && !fuel?.pricesOutOfDate));
  if (!plan || plan.inputsChanged || !fuel) return { key: '', stops: [] };
  const current = finite(progress?.progressMiles);
  const visits = fuel.stops.flatMap((saved: any, index: number) => {
    const stop = refreshing
      ? {
          ...saved,
          warning: 'Saved fuel stop. Route and fuel plan need updating.',
          accessOnly: true,
          buyGallons: 0,
          arrivalGallons: null,
          departureGallons: null,
          purchaseCostUsd: null,
          currentRouteMile: null,
          milesAhead: null,
          estimatedArrival: null,
        }
      : saved;
    const routeMile = finite(stop.currentRouteMile),
      serverMiles = finite(stop.milesAhead);
    if (
      !stop.stationId ||
      !Number.isFinite(stop.buyGallons) ||
      (stop.buyGallons <= 0 && !stop.accessOnly) ||
      (routeMile !== null && current !== null && routeMile < current - 0.5) ||
      (routeMile === null && serverMiles !== null && serverMiles < 0)
    )
      return [];
    return [
      {
        id: stop.stationId,
        warning: stop.warning ?? '',
        accessOnly: stop.accessOnly === true,
        number:
          typeof stop.number === 'number' &&
          Number.isInteger(stop.number) &&
          stop.number > 0
            ? stop.number
            : index + 1,
        visitKey: stop.visitKey ?? '',
        beforeStopId: stop.beforeStopId ?? null,
        gallons: stop.buyGallons,
        arrivalGallons: finite(stop.arrivalGallons),
        departureGallons: finite(stop.departureGallons),
        purchaseCostUsd: finite(stop.purchaseCostUsd),
        point: stop.point,
        name: stop.name,
        address: stop.address,
        yourPrice: finite(stop.yourPrice),
        priceDate: stop.priceDate ?? '',
        estimatedArrival: stop.estimatedArrival ?? null,
        priceEstimated: stop.priceEstimated === true,
        currency: stop.currency,
        tankGallons: finite(plan.tankGallons),
        unit: stop.unit ?? '',
        full: stop.fillToTarget,
        sent: stop.sent ? { changed: stop.sent.changed === true } : null,
        routeMile,
        miles:
          current !== null && routeMile !== null
            ? Math.max(0, routeMile - current)
            : serverMiles,
      },
    ];
  });
  visits.sort(
    (a: any, b: any) => (a.miles ?? Infinity) - (b.miles ?? Infinity),
  );
  /** @type {Map<string, typeof visits[number] & { visits: typeof visits }>} */
  const stations = new Map();
  for (const visit of visits) {
    const entry = stations.get(visit.id);
    if (entry) entry.visits.push(visit);
    else stations.set(visit.id, { ...visit, visits: [visit] });
  }
  const stops = [...stations.values()].map((stop: any) => ({
    ...stop,
    numbers: stop.visits.map((visit: any) => visit.number).join('/'),
  }));
  return { key: JSON.stringify(stops), stops };
}
