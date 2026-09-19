// @ts-check

/**
 * @param {import('../contracts.d.ts').MapPlan | null} current
 * @param {import('../contracts.d.ts').RoutePayload} payload
 * @returns {{ accepted: true, plan: import('../contracts.d.ts').MapPlan | null } | { accepted: false }}
 */
export function mergeRoutePayload(current, payload) {
  if (!payload?.geometryOmitted) return { accepted: true, plan: payload };
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
