import type { MapPlan, RoutePayload } from '../contracts.d.ts';

// A payload without geometry is only accepted for the plan it belongs to:
// the road it leaves out is the one already on the map.
export function mergeRoutePayload(
  current: MapPlan | null,
  payload: RoutePayload,
): { accepted: true; plan: MapPlan | null } | { accepted: false } {
  // Geometry not omitted means the payload carries the road itself, which
  // is what makes it a plan rather than the metadata around one.
  if (!payload?.geometryOmitted)
    return {
      accepted: true,
      plan: payload as MapPlan,
    };
  if (
    !current ||
    payload.id !== current.id ||
    payload.version !== current.version ||
    payload.truckId !== current.truckId ||
    payload.dispatchId !== current.dispatchId ||
    (payload.executionLegId ?? null) !== (current.executionLegId ?? null) ||
    (payload.assignmentRevision ?? 0) !== (current.assignmentRevision ?? 0)
  )
    return { accepted: false };
  return {
    accepted: true,
    plan: {
      ...payload,
      geometryOmitted: false,
      route: current.route,
      referenceRoute: current.referenceRoute,
    },
  };
}
