import type { MapPlan, PlanStop } from '../contracts.d.ts';

// Every stop of a plan, in order, counted once.
export function orderedStops(plan: MapPlan | null | undefined): PlanStop[] {
  return [
    ...new Map(
      [...(plan?.referenceStops || []), ...(plan?.stops || [])].map(stop => [
        stop.id,
        stop,
      ]),
    ).values(),
  ];
}

// Those of them still to be driven.
export function pendingStops(plan: MapPlan | null | undefined): PlanStop[] {
  const passed = new Set(plan?.tracking?.passedStopIds || []);
  return orderedStops(plan).filter(stop => !passed.has(stop.id));
}
