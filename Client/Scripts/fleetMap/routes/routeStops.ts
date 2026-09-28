import { onStopBadgesChanged, stopBadge } from './stopBadges.ts';
import type { LoadReference, PlanStop, RoutePoint } from '../contracts.d.ts';
import type { StopEtaLabel } from './stopEtaLabels.ts';
import type { StopFacts } from './stopCardContent.ts';
import { orderedStops } from './pendingStops.ts';
import { stopVisits } from './stopVisits.ts';
import { stopContent, stopDetails } from './stopCardContent.ts';
import { distanceLabel } from '../ui/distanceLabel.ts';

const point = (p: RoutePoint) => ({ lat: p.latitude, lng: p.longitude });
const text = (value: unknown) =>
  typeof value === 'string' && value.trim() ? value.trim() : undefined;

// One stop as this layer holds it: the stop itself, the marker drawn for
// it, the facts its card is built from, and the card once it has been
// built.
type Entry = {
  stop: PlanStop;
  marker: any;
  details?: StopFacts;
  miles: number;
  remaining: string | null;
  route: { total: string; percent: number } | null;
  content?: HTMLElement;
  contentKey?: string;
  [key: string]: unknown;
};

export function createRouteStops(
  map: google.maps.Map,
  StopMarker: any,
  popup: { show(content: Node, position: unknown): void; hide(): void },
  onOpen: (position: google.maps.LatLngLiteral) => void,
  formatDistance: (miles: number) => string = distanceLabel,
) {
  const entries = new Map<string, Entry>();
  let progress: number | null = null,
    selectedId: string | null = null;
  // Set by a layout that shows a chosen stop in its own panel (the
  // trip panel): a badge press then chooses the stop there
  // instead of opening the stop card. The focused stop is highlighted.
  let chooser: ((stopId: string) => void) | null = null;
  let focusedId: string | null = null;
  const highlight = new Map<string, boolean>();
  let etaLabels = new Map<string, StopEtaLabel>();
  let dispatchId: string | null = null,
    loadReference: LoadReference | null = null;
  let fuelArrivals: any[] = [];
  // The stops the server says are completed, and when; not GPS passage.
  let completions = new Map<string, string | null>();
  // The chain's numbers can arrive after the route: relabel in place.
  const stopBadgesWatch = onStopBadgesChanged(() => {
    for (const entry of entries.values()) {
      const label = stopBadge(entry.stop.id, entry.stop.job);
      entry.marker.setNumber?.(label);
      if (entry.details) {
        entry.details = { ...entry.details, number: label };
        entry.metadata = JSON.stringify(entry.details);
      }
      refreshContent(entry);
    }
  });

  function updateDistance(entry: Entry) {
    const valid = Number.isFinite(progress) && Number.isFinite(entry.miles);
    entry.remaining = valid
      ? formatDistance(Math.max(0, entry.miles - progress!))
      : null;
    entry.route =
      valid && entry.miles > 0
        ? {
            total: formatDistance(entry.miles),
            // Whole percent, so a sample that moves no figure on the card
            // does not rebuild it.
            percent: Math.round(
              Math.min(1, Math.max(0, progress! / entry.miles)) * 100,
            ),
          }
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
    const completion = completions.has(entry.stop.id)
      ? { at: completions.get(entry.stop.id) ?? null }
      : null;
    const key = JSON.stringify([
      completion,
      entry.metadata,
      loadReference,
      etaText,
      etaStatus,
      cycleStatus,
      etaTone,
      etaLabel,
      hours,
      entry.remaining,
      entry.route,
      fuelText,
    ]);
    if (entry.contentKey === key && !opening) return;
    if (entry.contentKey !== key) {
      entry.content = stopContent(
        { ...entry.details!, done: !!completion },
        {
          completion,
          loadReference,
          etaText,
          etaStatus,
          cycleStatus,
          etaTone: etaTone ?? undefined,
          remaining: entry.remaining ?? undefined,
          route: entry.route,
          etaLabel,
          hours,
          fuelText,
        },
      );
      entry.contentKey = key;
    }
    popup.show(entry.content!, point(entry.stop.point!));
  }

  // The chosen stop wears the reticle: the one whose card is open, else
  // the one the chain focused.
  function markSelected() {
    for (const [id, entry] of entries)
      entry.marker.selected = id === (selectedId ?? focusedId);
  }

  function show(entry: Entry) {
    selectedId = entry.stop.id;
    markSelected();
    onOpen(point(entry.stop.point!));
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
              truck: text(value.truck),
              trailer: text(value.trailer),
              driver: text(value.driver),
            }
          : null;
      for (const entry of entries.values()) refreshContent(entry);
    },
    setCompletions(list: { id: string; at: string | null }[]) {
      completions = new Map(
        (Array.isArray(list) ? list : []).map(x => [x.id, x.at ?? null]),
      );
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
      const labels = ordered.map(stop => stopBadge(stop.id, stop.job));
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
            number: labels[index],
            order: index + 1,
            job: stop.job,
          });
          entry = {
            marker,
            stop,
            miles: Number.NaN,
            metadata: null,
            remaining: null,
            route: null,
            content: undefined,
            contentKey: undefined,
          };
          const selected: Entry = entry;
          marker.onSelect = () =>
            chooser ? chooser(selected.stop.id) : show(selected);
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
          labels[index],
          completed,
        );
        row.metadata = JSON.stringify(row.details);
        row.marker.setNumber?.(labels[index]);
        row.marker.setOrder?.(index + 1);
        row.marker.setJob?.(stop.job);
        row.marker.setDone?.(completed);
        row.marker.setNext?.(stop.id === nextId, plan?.truckId ?? null);
        highlight.set(stop.id, stop.id === nextId || stop.id === previousId);
        row.marker.highlighted =
          highlight.get(stop.id) || stop.id === focusedId;
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
    // Opens one stop's card as a click on its badge would, and says where
    // it stands so the camera can go there.
    open(stopId: string) {
      const entry = entries.get(stopId);
      if (!entry?.stop.point) return null;
      show(entry);
      return point(entry.stop.point);
    },
    setChooser(value: ((stopId: string) => void) | null) {
      chooser = value;
    },
    // Highlights one stop as chosen, or none; the camera does not move.
    focus(stopId: string | null) {
      focusedId = stopId;
      for (const [id, entry] of entries)
        entry.marker.highlighted = highlight.get(id) || id === focusedId;
      markSelected();
    },
    setProgress(value: number | null) {
      progress = value;
      for (const entry of entries.values()) updateDistance(entry);
    },
    close() {
      selectedId = null;
      popup.hide();
      markSelected();
    },
    dispose() {
      stopBadgesWatch();
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
