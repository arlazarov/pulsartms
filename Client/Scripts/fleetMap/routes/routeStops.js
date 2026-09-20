import { orderedStops } from './pendingStops.js';
import { stopVisits } from './stopVisits.js';
import { distanceLabel } from '../ui/distanceLabel.js';
import { stopAppointment } from './stopAppointment.js';
import { addressLines } from '../ui/addressLines.js';
import { stopAppointmentReference } from './stopAppointmentReference.js';
import { loadReferenceContent } from '../ui/loadReferenceContent.js';

const point = p => ({ lat: p.latitude, lng: p.longitude });

export function createRouteStops(
  map,
  StopMarker,
  popup,
  onOpen,
  formatDistance = distanceLabel,
) {
  const entries = new Map();
  let progress = null,
    selectedId = null;
  let etaLabels = new Map();
  let dispatchId = null,
    loadReference = null;
  let fuelArrivals = [];

  function updateDistance(entry) {
    const valid = Number.isFinite(progress) && Number.isFinite(entry.miles);
    entry.remaining = valid
      ? formatDistance(Math.max(0, entry.miles - progress))
      : null;
    refreshContent(entry);
  }

  function refreshContent(entry, opening = false) {
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
      entry.content = stopContent(
        entry.details,
        loadReference,
        etaText,
        etaStatus,
        cycleStatus,
        etaTone,
        entry.remaining,
        etaLabel,
        hours,
        fuelText,
      );
      entry.contentKey = key;
    }
    popup.show(entry.content, point(entry.stop.point));
  }

  function show(entry) {
    selectedId = entry.stop.id;
    onOpen();
    refreshContent(entry, true);
  }

  return {
    refreshDistances() {
      for (const entry of entries.values()) updateDistance(entry);
    },
    setLoadReference(value) {
      if (value && (!dispatchId || value.dispatchId !== dispatchId)) return;
      loadReference =
        value && Number.isInteger(value.loadNumber) && value.loadNumber > 0
          ? {
              loadNumber: value.loadNumber,
              loadLabel:
                typeof value.loadLabel === 'string'
                  ? value.loadLabel
                  : String(value.loadNumber),
              orderNumber:
                typeof value.orderNumber === 'string' &&
                value.orderNumber.trim()
                  ? value.orderNumber
                  : null,
            }
          : null;
      for (const entry of entries.values()) refreshContent(entry);
    },
    setEtas(labels) {
      etaLabels = labels;
      for (const entry of entries.values()) updateDistance(entry);
    },
    setPlan(plan) {
      const active = new Set();
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
            miles: null,
            metadata: null,
            remaining: null,
            content: null,
            contentKey: null,
          };
          const selected = entry;
          marker.onSelect = () => show(selected);
          entries.set(stop.id, entry);
        }
        const completed = passed.has(stop.id);
        if (completed && !entry.completed && selectedId === stop.id)
          this.close();
        entry.completed = completed;
        entry.stop = stop;
        entry.details = stopDetails(
          stop,
          detailsHref,
          visits.get(stop.id),
          `${index + 1}`,
          completed,
        );
        entry.metadata = JSON.stringify(entry.details);
        entry.marker.setNumber?.(`${index + 1}`);
        entry.marker.setJob?.(stop.job);
        entry.marker.setDone?.(completed);
        entry.marker.highlighted = stop.id === nextId || stop.id === previousId;
        const stopIndex = plan.stops.findIndex(s => s.id === stop.id);
        entry.miles =
          completed || stopIndex < 0
            ? null
            : plan.route.legs
                .slice(0, stopIndex + (plan.fromCurrentPosition ? 1 : 0))
                .reduce((sum, leg) => sum + leg.miles, 0);
        updateDistance(entry);
      }
      for (const [id, entry] of entries) {
        if (active.has(id)) continue;
        entry.marker.map = null;
        entries.delete(id);
        if (selectedId === id) this.close();
      }
    },
    setProgress(value) {
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

function stopDetails(stop, detailsHref, visit, number, done) {
  return {
    number: number ?? '',
    done: done === true,
    job: stop.job || '',
    stateAfter: stop.stateAfter || 'Unknown',
    name: stop.name || '',
    address: addressLines(stop.address),
    appointment: stopAppointment(stop),
    references: stopAppointmentReference(stop.notes, stop.job),
    detailsHref,
    position: visit ? `Load stop ${visit.number} of ${visit.count}` : '',
    visit:
      visit?.visitCount > 1
        ? `Visit ${visit.visitNumber} of ${visit.visitCount}`
        : '',
  };
}

function stopContent(
  stop,
  loadReference,
  etaText,
  etaStatus,
  cycleStatus,
  etaTone,
  remaining,
  etaLabel,
  hours,
  fuelText,
) {
  function element(tag, className, text) {
    const node = document.createElement(tag);
    node.className = className;
    if (text) node.textContent = text;
    return node;
  }
  const details = element('div', 'fleet-route-popup fleet-route-popup--stop');
  const location = element('div', 'fleet-route-popup__location');
  const information = element('div', 'fleet-route-popup__information');
  if (loadReference) location.append(loadReferenceContent(loadReference));
  const address = element('div', 'fleet-route-popup__address');
  address.append(
    element('span', 'fleet-route-popup__address-line', stop.address.street),
  );
  if (stop.address.locality)
    address.append(
      element('span', 'fleet-route-popup__address-line', stop.address.locality),
    );
  // The card opens with what a dispatcher scans for: which stop this is in
  // the run, what happens there, and whether it is already behind them.
  const head = element('div', 'fleet-route-popup__head');
  if (stop.number)
    head.append(
      element(
        'span',
        `fleet-route-popup__number${stop.done ? ' is-done' : ''}`,
        stop.number,
      ),
    );
  head.append(element('span', 'fleet-route-popup__job', stop.job));
  const state = stop.done ? 'Done' : etaStatus;
  if (state)
    head.append(
      element(
        'span',
        `fleet-route-popup__state fleet-route-popup__state--${
          stop.done || etaTone === 'success'
            ? 'success'
            : etaTone === 'danger'
              ? 'danger'
              : 'neutral'
        }`,
        state,
      ),
    );
  const kind = element('div', 'fleet-route-popup__kind', stop.position);
  if (stop.stateAfter && stop.stateAfter !== 'Unknown')
    kind.append(element('span', '', `After: ${stop.stateAfter}`));
  if (stop.visit) {
    const visit = element('span', 'fleet-route-popup__visit', stop.visit);
    visit.title = `${stop.visit} at this address`;
    kind.append(visit);
  }
  location.append(
    head,
    kind,
    element('strong', 'fleet-route-popup__company', stop.name),
    address,
  );
  if (stop.references.length) {
    const references = element(
      'div',
      'fleet-route-popup__reference fleet-route-popup__section-start',
    );
    references.append(
      element('span', 'fleet-route-popup__label', 'Appt #'),
      element('span', '', stop.references.join(', ')),
    );
    location.append(references);
  }
  const facts = element('dl', 'fleet-route-popup__facts');
  function field(parent, label, text, className) {
    const group = element('div', `fleet-route-popup__field ${className}`);
    const value = element('dd', 'fleet-route-popup__value');
    value.append(element('span', '', text));
    group.append(element('dt', 'fleet-route-popup__label', label), value);
    parent.append(group);
    return value;
  }
  field(
    facts,
    'Appointment',
    stop.appointment,
    'fleet-route-popup__appointment',
  );
  const eta = field(
    facts,
    etaLabel,
    etaText,
    `fleet-route-popup__eta${etaTone ? ` fleet-route-popup__eta--${etaTone}` : ''}`,
  );
  if (cycleStatus) {
    eta.className += ' fleet-route-popup__value--cycle';
    eta.children[0].className = 'fleet-route-popup__arrival';
    if (etaStatus)
      eta.children[0].append(
        element('span', 'fleet-route-popup__status', etaStatus),
      );
    eta.append(
      element(
        'span',
        'fleet-route-popup__status fleet-route-popup__cycle-status',
        cycleStatus,
      ),
    );
  } else if (etaStatus)
    eta.append(element('span', 'fleet-route-popup__status', etaStatus));
  // What is left to this stop, and the card calls it Left. Labelling it
  // Total said the length of the whole run, which it is not.
  field(
    facts,
    'Left',
    remaining ?? '—',
    'fleet-route-popup__distance fleet-route-popup__section-start',
  );
  // The card says fuel as a named figure on its line, not as a dial. This is
  // one more fact about the stop, so it reads as one: the same label column
  // as the appointment and the ETA above it.
  if (fuelText !== null) {
    const value = field(
      facts,
      'Fuel on arrival',
      fuelText === '—' ? '—' : `${fuelText.percent}% · ${fuelText.quantity}`,
      'fleet-route-popup__fuel fleet-route-popup__section-start',
    );
    value.title = 'Estimated from the current fuel plan';
  }
  information.append(facts);
  if (hours?.length) {
    const block = element('div', 'stop-hours stop-hours--inline');
    const cycle = element('section', 'stop-hours__cycle');
    cycle.setAttribute('aria-label', 'Cycle at this stop');
    for (const [index, row] of hours.entries()) {
      const line = element(
        'div',
        `stop-hours__row${row.recap ? ' stop-hours__recap' : ''}`,
      );
      const value = element(
        'span',
        `stop-hours__value${row.tone === 'danger' ? ' stop-hours__value--danger' : ''}`,
        row.value,
      );
      if (row.credit) value.append(element('span', '', row.credit));
      if (row.status)
        value.append(
          element(
            'span',
            `stop-hours__status stop-hours__status--${row.tone === 'neutral' ? 'muted' : row.tone}`,
            row.status,
          ),
        );
      const label =
        index === 0
          ? element('h3', 'stop-hours__heading', row.label)
          : element('span', 'stop-hours__label', row.label);
      if (row.title) label.title = row.title;
      line.append(label, value);
      cycle.append(line);
    }
    block.append(cycle);
    information.append(block);
  }
  if (stop.detailsHref) {
    const link = element(
      'a',
      'fleet-route-popup__details-link',
      // The arrow belongs to the last word; on its own line it reads as a
      // stray mark rather than as a link that leaves the map.
      'Route & load details ↗',
    );
    link.href = stop.detailsHref;
    information.append(link);
  }
  details.append(location, information);
  return details;
}
