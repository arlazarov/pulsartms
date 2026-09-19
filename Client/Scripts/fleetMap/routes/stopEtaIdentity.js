// @ts-check
import { pendingStops } from './pendingStops.js';

/** @typedef {import('../contracts.d.ts').RouteMetadata & {tracking?: ({allStopsPassed?: boolean} & import('../contracts.d.ts').RouteTracking) | null}} EtaPlan */

/** @param {EtaPlan | null | undefined} plan */
export function etaStops(plan) {
  return plan?.tracking?.allStopsPassed ? [] : pendingStops(plan);
}

/** @param {EtaPlan | null | undefined} plan */
export function stopEtaIdentity(plan) {
  if (!plan) return '';
  return JSON.stringify([plan.truckId, plan.dispatchId, etaStops(plan).map(stop => [stop.id,
    stop.point?.latitude, stop.point?.longitude, stop.address, stop.job,
    stop.scheduledDate, stop.scheduledTime, stop.scheduledDate2, stop.scheduledTime2])]);
}
