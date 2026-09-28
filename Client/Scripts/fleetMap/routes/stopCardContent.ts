import { stopAppointment } from './stopAppointment.ts';
import { addressLines } from '../ui/addressLines.ts';
import { stopAppointmentReference } from './stopAppointmentReference.ts';
import { loadReferenceContent } from '../ui/loadReferenceContent.ts';
import { distanceLabel } from '../ui/distanceLabel.ts';

// What a stop's card says, as plain data: the facts the card is built from
// and the words under them. Nothing here touches the map or the DOM - it is
// the sentence, not the drawing.

import type { PlanStop } from '../contracts.d.ts';
import type { LoadReference } from '../contracts.d.ts';
import type { StopVisit } from './stopVisits.ts';
import type { HoursRow } from './stopHoursLabels.ts';

// The facts a stop's card is built from.
export type StopFacts = {
  number: string | number;
  done: boolean;
  job: string;
  stateAfter: string;
  name: string;
  address: { street: string; locality: string };
  appointment: string;
  references: string[];
  detailsHref: string;
  position: string;
  visit: string;
};

export function stopDetails(
  stop: PlanStop & { stateAfter?: string },
  detailsHref: string,
  visit: StopVisit | undefined,
  number: string | number | undefined,
  done: boolean,
): StopFacts {
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
      (visit?.visitCount ?? 0) > 1
        ? `Visit ${visit!.visitNumber} of ${visit!.visitCount}`
        : '',
  };
}

// What the card says about the stop, beside the facts themselves: when the
// truck gets there, what that does to the cycle, and what it will have in
// the tank. Eleven of these arrived as positional arguments, and adding one
// meant counting commas at every call.
export type StopCardWords = {
  loadReference?: LoadReference | null;
  etaText?: string;
  etaStatus?: string;
  cycleStatus?: string;
  etaTone?: string;
  remaining?: string;
  etaLabel?: string;
  hours?: HoursRow[] | null;
  // What the tank will hold on arrival: the two figures, the em dash when
  // the plan cannot say, or null when there is nothing to show at all.
  fuelText?: { percent: number; quantity: string } | '—' | null;
  // Set only when the server says the stop is completed: no forecast is
  // left, only when it was done, if that is known.
  completion?: { at: string | null } | null;
  // The whole planned road to this stop and the share of it already
  // driven, from the route's own progress.
  route?: { total: string; percent: number } | null;
};

export function stopContent(
  stop: StopFacts,
  {
    loadReference,
    etaText,
    etaStatus,
    cycleStatus,
    etaTone,
    remaining,
    etaLabel,
    hours,
    fuelText,
    completion,
    route,
  }: StopCardWords,
): HTMLElement {
  function element(tag: string, className: string, text?: string) {
    const node = document.createElement(tag);
    node.className = className;
    if (text) node.textContent = text;
    return node;
  }
  const details = element('div', 'fleet-route-popup fleet-route-popup--stop');
  const location = element('div', 'fleet-route-popup__location');
  const information = element('div', 'fleet-route-popup__information');
  // The card opens with what a dispatcher scans for: which stop this is in
  // the run and what happens there, then whose load it is. Whether it is
  // on time is said once, beside its ETA; a stop behind the truck says Done.
  const head = element('div', 'fleet-route-popup__head');
  if (stop.number) {
    const badge = element(
      'span',
      `fleet-route-popup__number${stop.done ? ' is-done' : ''}`,
      String(stop.number),
    );
    head.append(badge);
  }
  const identity = element('div', 'fleet-route-popup__identity');
  const job = element('strong', 'fleet-route-popup__job', stop.job);
  if (stop.done)
    job.append(
      element(
        'span',
        'fleet-route-popup__state fleet-route-popup__state--success',
        completion ? 'Completed' : 'Done',
      ),
    );
  identity.append(job);
  if (loadReference)
    identity.append(loadReferenceContent(loadReference, stop.detailsHref));
  else if (stop.detailsHref) {
    // Until the load's number is known the head still opens it, in words.
    const link = element(
      'a',
      'fleet-route-popup__load-link',
      'Open load\u00a0↗',
    ) as HTMLAnchorElement;
    link.href = stop.detailsHref;
    link.title = 'Open load';
    identity.append(link);
  }
  const kind = element('div', 'fleet-route-popup__kind', stop.position);
  if (stop.stateAfter && stop.stateAfter !== 'Unknown')
    kind.append(element('span', '', `After: ${stop.stateAfter}`));
  if (stop.visit) {
    const visit = element('span', 'fleet-route-popup__visit', stop.visit);
    visit.title = `${stop.visit} at this address`;
    kind.append(visit);
  }
  identity.append(kind);
  head.append(identity);
  const address = element('div', 'fleet-route-popup__address');
  address.append(
    element('span', 'fleet-route-popup__address-line', stop.address.street),
  );
  if (stop.address.locality)
    address.append(
      element('span', 'fleet-route-popup__address-line', stop.address.locality),
    );
  location.append(
    head,
    element('strong', 'fleet-route-popup__company', stop.name),
    address,
  );
  if (stop.references.length) {
    const references = element('div', 'fleet-route-popup__reference');
    references.append(
      element('span', 'fleet-route-popup__label', 'Appt #'),
      element('span', '', stop.references.join(', ')),
    );
    location.append(references);
  }
  // Who takes the truck there, as the truck card names them.
  const crew = [
    loadReference?.truck ? `Truck ${loadReference.truck}` : '',
    loadReference?.trailer ? `Trailer ${loadReference.trailer}` : '',
  ].filter(Boolean);
  if (crew.length || loadReference?.driver) {
    const assignment = element('div', 'fleet-route-popup__assignment');
    if (crew.length) assignment.append(element('span', '', crew.join(' · ')));
    if (loadReference?.driver)
      assignment.append(element('span', '', loadReference.driver));
    location.append(assignment);
  }
  const facts = element('dl', 'fleet-route-popup__facts');
  function field(
    parent: HTMLElement,
    label: string,
    text: string,
    className: string,
  ) {
    const group = element('div', `fleet-route-popup__field ${className}`);
    const value = element('dd', 'fleet-route-popup__value');
    value.append(element('span', '', text));
    group.append(element('dt', 'fleet-route-popup__label', label), value);
    parent.append(group);
    return value;
  }
  if (completion) {
    // A completed stop keeps its booking and says when it was done; the
    // forecast, distance, fuel and cycle all looked ahead and are gone.
    field(
      facts,
      'Appointment',
      stop.appointment,
      'fleet-route-popup__appointment',
    );
    if (completion.at)
      field(
        facts,
        'Completed at',
        completion.at,
        'fleet-route-popup__completed',
      );
    information.append(facts);
    details.append(location, information);
    return details;
  }
  // The forecast leads, with the one word about it: on time, late, short
  // of cycle. The booking follows on the same label column.
  const eta = field(
    facts,
    etaLabel ?? 'ETA',
    etaText ?? '',
    `fleet-route-popup__eta${etaTone ? ` fleet-route-popup__eta--${etaTone}` : ''}`,
  );
  if (etaStatus && !stop.done)
    eta.append(
      element(
        'span',
        `fleet-route-popup__status fleet-route-popup__status--${
          etaTone === 'danger' ? 'danger' : 'success'
        }`,
        etaStatus,
      ),
    );
  if (cycleStatus)
    eta.append(
      element(
        'span',
        'fleet-route-popup__status fleet-route-popup__cycle-status',
        cycleStatus,
      ),
    );
  field(
    facts,
    'Appointment',
    stop.appointment,
    'fleet-route-popup__appointment',
  );
  // What is left to this stop, and the card calls it Left. Labelling it
  // Total said the length of the whole run, which it is not.
  field(
    facts,
    'Left',
    remaining ?? '—',
    'fleet-route-popup__distance fleet-route-popup__section-start',
  );
  // The whole road to the stop, and how much of it is behind the truck
  // (the owner, September 27).
  if (route) {
    const percent = route.percent;
    field(facts, 'Route', route.total, 'fleet-route-popup__route');
    const bar = element('div', 'fleet-route-popup__progress');
    const meter = element(
      'progress',
      'fleet-route-popup__progress-bar',
    ) as HTMLProgressElement;
    meter.max = 100;
    meter.value = percent;
    meter.title = 'Route driven';
    bar.append(meter, element('small', '', `${percent}% driven`));
    facts.append(bar);
  }
  // The card says fuel as a named figure on its line, not as a dial. This is
  // one more fact about the stop, so it reads as one: the same label column
  // as the appointment and the ETA above it.
  if (fuelText !== null) {
    const tank =
      fuelText === '—' || !fuelText
        ? '—'
        : `${fuelText.percent}% · ${fuelText.quantity}`;
    const value = field(
      facts,
      'Fuel on arrival',
      tank,
      'fleet-route-popup__fuel',
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
  details.append(location, information);
  return details;
}
