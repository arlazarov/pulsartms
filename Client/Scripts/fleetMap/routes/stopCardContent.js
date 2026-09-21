import { stopAppointment } from './stopAppointment.js';
import { addressLines } from '../ui/addressLines.js';
import { stopAppointmentReference } from './stopAppointmentReference.js';
import { loadReferenceContent } from '../ui/loadReferenceContent.js';
import { distanceLabel } from '../ui/distanceLabel.ts';

// What a stop's card says, as plain data: the facts the card is built from
// and the words under them. Nothing here touches the map or the DOM - it is
// the sentence, not the drawing.

export function stopDetails(stop, detailsHref, visit, number, done) {
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

export function stopContent(
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
