import type { MapPlan } from '../contracts.d.ts';
import type { StopEtaLabel } from './stopEtaLabels.ts';
import { stopEtaDeadline, stopEtaLabels } from './stopEtaLabels.ts';
import { etaStops, stopEtaIdentity } from './stopEtaIdentity.ts';

// What the stops are showing, and until when. A forecast is good until its
// own validUntil; while a new one is being worked out the old one may be
// shown a while longer, up to the grace deadline, rather than blanking
// every arrival time on the route.
type Snapshot = {
  labels: Map<string, StopEtaLabel>;
  context: string;
  fingerprint: string;
  calculatedAt: number;
  validUntil: number;
  graceUntil: number;
};

/**
 * The arrival times the stops show. The server sends forecasts for a whole
 * chain of loads; this keeps the ones for the plan on screen, decides
 * whether a new one replaces what is held or is only a refresh of it, and
 * takes them all down when they have gone stale.
 */
export function createStopEtaWindow(
  show: (labels: Map<string, StopEtaLabel>, refreshing: boolean) => void,
  isDisposed: () => boolean,
) {
  let expiry: ReturnType<typeof setTimeout> | null = null;
  let snapshot: Snapshot | null = null;
  let refreshing = false;
  let version = 0;

  function clear() {
    if (expiry !== null) clearTimeout(expiry);
    expiry = null;
    snapshot = null;
    refreshing = false;
    version++;
    show(new Map(), false);
  }

  function refresh() {
    if (expiry !== null) clearTimeout(expiry);
    const current = ++version;
    const remaining =
      (refreshing ? snapshot?.graceUntil : snapshot?.validUntil)! - Date.now();
    if (!(remaining > 0)) {
      clear();
      return;
    }
    show(snapshot!.labels, refreshing);
    expiry = setTimeout(
      () => {
        if (!isDisposed() && current === version) clear();
      },
      Math.min(remaining, 2147483647),
    );
  }

  return {
    clear,
    refresh,
    held: () => snapshot !== null,
    // The timer only. Used while tearing down, where telling the stops
    // anything would be telling a layer that has gone.
    stop() {
      if (expiry !== null) clearTimeout(expiry);
      expiry = null;
      snapshot = null;
    },
    // A plan that is no longer the same work cannot keep the last one's
    // arrival times.
    forgetIfPlanChanged(plan: MapPlan | null) {
      if (snapshot && snapshot.context !== stopEtaIdentity(plan)) clear();
    },
    receive(payload: any, plan: MapPlan | null) {
      if (!payload) {
        clear();
        return;
      }
      // A forecast for other work, or for a version of this one that has
      // since been replaced, says nothing about what is on screen.
      if (
        payload.planId !== plan?.id ||
        payload.planVersion !== plan?.version ||
        payload.truckId !== plan?.truckId ||
        payload.currentDispatchId !== plan?.dispatchId ||
        (payload.currentExecutionLegId ?? null) !==
          (plan?.executionLegId ?? null) ||
        (payload.currentAssignmentRevision ?? 0) !==
          (plan?.assignmentRevision ?? 0)
      )
        return;
      const active = new Set(etaStops(plan).map(stop => stop.id));
      const currentStops = (payload.eta?.stops || []).filter(
        (stop: any) =>
          stop.dispatchId === payload.currentDispatchId &&
          active.has(stop.stopId),
      );
      const pending =
        payload.refreshing === true || payload.eta?.routeUpdatePending === true;
      const labels = stopEtaLabels({
        ...payload.eta,
        stops: currentStops,
        routeUpdatePending: pending,
      });
      const currentLabels = new Map<string, StopEtaLabel>();
      for (const stop of currentStops) {
        const label = labels.get(`${stop.dispatchId}:${stop.stopId}`);
        if (label) currentLabels.set(stop.stopId, label);
      }
      const fingerprint = JSON.stringify([
        payload.eta?.calculatedAt,
        payload.eta?.validUntil,
        currentStops,
        payload.eta?.cycleAtCalculation,
      ]);
      const complete =
        currentLabels.size > 0 &&
        (!refreshing ||
          !snapshot ||
          [...snapshot.labels.keys()].every(id => currentLabels.has(id)));
      const calculatedAt = Date.parse(payload.eta?.calculatedAt ?? '');
      if (
        !pending &&
        currentLabels.size > 0 &&
        calculatedAt < snapshot?.calculatedAt!
      )
        return;
      const capture = (): Snapshot => ({
        labels: currentLabels,
        context: stopEtaIdentity(plan),
        fingerprint,
        calculatedAt,
        validUntil: Date.parse(payload.eta.validUntil),
        graceUntil: stopEtaDeadline({
          ...payload.eta,
          routeUpdatePending: true,
        }),
      });
      if (
        !pending &&
        complete &&
        Date.parse(payload.eta.validUntil) > Date.now()
      ) {
        if (snapshot?.fingerprint !== fingerprint) snapshot = capture();
        refreshing = false;
      } else if (pending || (refreshing && !payload.eta)) {
        refreshing = true;
        // Pending data cannot replace the deadline or recap/status rows.
        if (!snapshot && currentLabels.size > 0) snapshot = capture();
      } else {
        clear();
        return;
      }
      refresh();
    },
  };
}
