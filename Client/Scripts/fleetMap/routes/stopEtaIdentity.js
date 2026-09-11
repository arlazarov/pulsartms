import { pendingStops } from './pendingStops.js';

export function etaStops(plan) {
  return plan?.tracking?.allStopsPassed ? [] : pendingStops(plan);
}

export function stopEtaIdentity(plan) {
  if (!plan) return '';
  return JSON.stringify([plan.truckId, plan.dispatchId, etaStops(plan).map(stop => [stop.id,
    stop.point?.latitude, stop.point?.longitude, stop.address, stop.job,
    stop.scheduledDate, stop.scheduledTime, stop.scheduledDate2, stop.scheduledTime2])]);
}
