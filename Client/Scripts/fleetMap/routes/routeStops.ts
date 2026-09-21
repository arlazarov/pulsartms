import type { LoadReference, PlanStop, RoutePoint } from '../contracts.d.ts';
import type { StopEtaLabel } from './stopEtaLabels.ts';
import type { StopFacts } from './stopCardContent.ts';
import { orderedStops } from './pendingStops.ts';
import { stopVisits } from './stopVisits.ts';
import { stopContent, stopDetails } from './stopCardContent.ts';
import { distanceLabel } from '../ui/distanceLabel.ts';

const point = (p: RoutePoint) => ({ lat: p.latitude, lng: p.longitude });

// One stop as this layer holds it: the stop itself, the marker drawn for
// it, the facts its card is built from, and the card once it has been
// built.
type Entry = {
  stop: PlanStop;
  marker: any;
  details?: StopFacts;
  miles: number;
  remaining: string | null;
  content?: HTMLElement;
  contentKey?: string;
  [key: string]: unknown;
};

export function createRouteStops(
  map: google.maps.Map,
  StopMarker: any,
  popup: { show(content: Node, position: unknown): void; hide(): void },
  onOpen: () => void,
  formatDistance: (miles: number) => string = distanceLabel,
) {
  const entries = new Map<string, Entry>();
  let progress: number | null = null,
    selectedId: string | null = null;
  let etaLabels = new Map<string, StopEtaLabel>();
  let dispatchId: string | null = null,
    loadReference: LoadReference | null = null;
  let fuelArrivals: any[] = [];

  function updateDistance(entry: Entry) {
    const valid = Number.isFinite(progress) && Number.isFinite(entry.miles);
    entry.remaining = valid
      ? formatDistance(Math.max(0, entry.miles - progress!))
      : null;
    refreshContent(entry);
  }

  function refreshContent(entry: Entry, opening = false) {
    if (selectedId !== entry.stop.id) return;
    const eta = etaLabels.get(entry.stop.id);
    const etaText = eta?.arrivalText || eta?.text || '—';
    const etaStatus = eta?.arrivalStatusText ?? eta?.statusText ?? '';
    const cycleStatus = eta?.cycleStatusText || '';
    const etaTone =
      eta?.text && ['eta', 'success', 'danger'].includes(eta.tone)
        ? eta.tone
        : null;
    const hours = eta?.hours ?? null;
    const etaLabel = eta?.etaLabel || 'ETA';
    const arrival = fuelArrivals.find(
      x => x.dispatchId === dispatchId && x.stopId === entry.stop.id,
    );
    const fuelText =
      arrival &&
      Number.isFinite(arrival.gallons) &&
      Number.isFinite(arrival.percent) &&
      arrival.gallons >= 0 &&
      arrival.percent >= 0 &&
      arrival.percent <= 100
        ? {
            percent: Math.round(arrival.percent),
            quantity: `${arrival.gallons.toFixed(0)} US gal`,
          }
        : '—';
    const key = JSON.stringify([
      entry.metadata,
      loadReference,
      etaText,
      etaStatus,
      cycleStatus,
      etaTone,
      etaLabel,
      hours,
      entry.remaining,
      fuelText,
    ]);
    if (entry.contentKey === key && !opening) return;
    if (entry.contentKey !== key) {
      entry.content = stopContent(entry.details!, {
        loadReference,
        etaText,
        etaStatus,
        cycleStatus,
        etaTone: etaTone ?? undefined,
        remaining: entry.remaining ?? undefined,
        etaLabel,
        hours,
        fuelText,
      });
      entry.contentKey = key;
    }
    popup.show(entry.content!, point(entry.stop.point!));
  }

  function show(entry: Entry) {
    selectedId = entry.stop.id;
    onOpen();
    refreshContent(entry, true);
  }

  return {
    refreshDistances() {
      for (const entry of entries.values()) updateDistance(entry);
    },
    setLoadReference(value: (LoadReference & { dispatchId?: string }) | null) {
      if (value && (!dispatchId || value.dispatchId !== dispatchId)) return;
      loadReference =
        value && Number.isInteger(value.loadNumber) && value.loadNumber > 0
          ? {
              dispatchId: value.dispatchId ?? '',
              loadNumber: value.loadNumber,
              loadLabel:
                typeof value.loadLabel === 'string'
                  ? value.loadLabel
                  : String(value.loadNumber),
              orderNumber:
                typeof value.orderNumber === 'string' &&
                value.orderNumber.trim()
                  ? value.orderNumber
                  : undefined,
            }
          : null;
      for (const entry of entries.values()) refreshContent(entry);
    },
    setEtas(labels: Map<string, StopEtaLabel>) {
      etaLabels = labels;
      for (const entry of entries.values()) updateDistance(entry);
    },
    setPlan(plan: any) {
      const active = new Set<string>();
      const passed = new Set(plan?.tracking?.passedStopIds || []);
      if (dispatchId !== plan?.dispatchId) loadReference = null;
      dispatchId = plan?.dispatchId;
      fuelArrivals =
        plan?.fuelStopArrivals ??
        (plan?.fuelPlan &&
        (!plan.fuelPlan.needsRefresh || plan.fuelPlan.pricesOutOfDate)
          ? (plan.fuelPlan.stopArrivals ?? [])
          : []);
      const detailsHref =
        typeof dispatchId === 'string' &&
        /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(dispatchId) &&
        dispatchId !== '00000000-0000-0000-0000-000000000000'
          ? `/dispatch/${dispatchId}`
          : null;
      const visits = stopVisits(orderedStops(plan));
      // The two stops the truck is between: the one it has just left and the
      // one it is driving to. They are what a dispatcher looks for first, so
      // they are named on the map the way a picked load's stops are - unless
      // a truck is standing on one, where the mark it makes with the truck
      // says it already.
      const ordered = orderedStops(plan);
      const nextId = plan?.tracking?.nextStopId ?? null;
      const nextIndex = ordered.findIndex(stop => stop.id === nextId);
      const previousId =
        ordered
          .slice(0, nextIndex < 0 ? ordered.length : nextIndex)
          .filter(stop => passed.has(stop.id))
          .at(-1)?.id ?? null;
      for (const [index, stop] of ordered.entries()) {
        active.add(stop.id);
        let entry = entries.get(stop.id);
        if (!entry) {
          const marker = new StopMarker({
            map,
            position: point(stop.point),
            number: `${index + 1}`,
            job: stop.job,
          });
          entry = {
            marker,
            stop,
            miles: Number.NaN,
            metadata: null,
            remaining: null,
            content: undefined,
            contentKey: undefined,
          };
          const selected: Entry = entry;
          marker.onSelect = () => show(selected);
          entries.set(stop.id, entry);
        }
        const row: Entry = entry!;
        const completed = passed.has(stop.id);
        if (completed && !row.completed && selectedId === stop.id) this.close();
        row.completed = completed;
        row.stop = stop;
        row.details = stopDetails(
          stop,
          detailsHref ?? '',
          visits.get(stop.id),
          `${index + 1}`,
          completed,
        );
        row.metadata = JSON.stringify(row.details);
        row.marker.setNumber?.(`${index + 1}`);
        row.marker.setJob?.(stop.job);
        row.marker.setDone?.(completed);
        row.marker.highlighted = stop.id === nextId || stop.id === previousId;
        const stopIndex = plan.stops.findIndex(
          (s: PlanStop) => s.id === stop.id,
        );
        row.miles =
          completed || stopIndex < 0
            ? Number.NaN
            : plan.route.legs
                .slice(0, stopIndex + (plan.fromCurrentPosition ? 1 : 0))
                .reduce(
                  (sum: number, leg: { miles: number }) => sum + leg.miles,
                  0,
                );
        updateDistance(row);
      }
      for (const [id, entry] of entries) {
        if (active.has(id)) continue;
        entry.marker.map = null;
        entries.delete(id);
        if (selectedId === id) this.close();
      }
    },
    setProgress(value: number) {
      progress = value;
      for (const entry of entries.values()) updateDistance(entry);
    },
    close() {
      selectedId = null;
      popup.hide();
    },
    clear() {
      for (const entry of entries.values()) entry.marker.map = null;
      entries.clear();
      progress = null;
      etaLabels = new Map();
      dispatchId = null;
      loadReference = null;
      fuelArrivals = [];
      this.close();
    },
  };
}
