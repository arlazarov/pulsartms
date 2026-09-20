import { stationQuantity } from './stationQuantity.js';
import { distanceLabel } from '../ui/distanceLabel.js';

function node(tag, className, text) {
  const element = document.createElement(tag);
  element.className = className;
  if (text !== undefined) element.textContent = text;
  return element;
}

export function fuelGaugeValue(gallons, tankGallons) {
  if (
    !Number.isFinite(gallons) ||
    gallons < 0 ||
    !Number.isFinite(tankGallons) ||
    tankGallons <= 0 ||
    gallons > tankGallons
  )
    return null;
  return Math.round((gallons / tankGallons) * 100);
}

function gauge(label, gallons, tankGallons, station, unit) {
  const percent = fuelGaugeValue(gallons, tankGallons);
  const quantity = stationQuantity(gallons, station, unit, Math.round) || '—';
  return createFuelGauge(label, percent, quantity);
}

export function createFuelGauge(label, percent, quantity) {
  const group = node('div', 'fleet-fuel-visit__gauge');
  group.setAttribute(
    'aria-label',
    `${label}: ${percent === null ? '' : `${percent}%, `}${quantity}`,
  );
  const dial = node('span', 'driver-hours__dial fleet-fuel-visit__dial');
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 64 64');
  svg.setAttribute('aria-hidden', 'true');
  for (const [className, fill] of [
    ['driver-hours__track', null],
    ['driver-hours__arc', percent],
  ]) {
    const circle = document.createElementNS(
      'http://www.w3.org/2000/svg',
      'circle',
    );
    for (const [name, value] of Object.entries({
      class: className,
      cx: '32',
      cy: '32',
      r: '28',
      pathLength: '100',
    }))
      circle.setAttribute(name, value);
    if (className === 'driver-hours__arc')
      circle.setAttribute('stroke-dasharray', `${fill ?? 0} 100`);
    svg.append(circle);
  }
  dial.append(
    svg,
    node(
      'strong',
      'fleet-fuel-visit__percent',
      percent === null ? '—' : `${percent}%`,
    ),
  );
  group.append(
    node('span', 'fleet-fuel-visit__label', label),
    dial,
    node('span', 'fleet-fuel-visit__quantity', quantity),
  );
  return group;
}

export function fuelPurchaseCostLabel(costUsd) {
  return Number.isFinite(costUsd) && costUsd >= 0
    ? `≈ $${costUsd.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} USD`
    : '';
}

// A named figure, the way every other fact on these cards is written. This
// was one sentence about itself - "Estimated price: 6.112 USD/US gal · quote
// 2026-09-20" - which is a third way of saying a labelled number.
export function plannedPrice(visit) {
  if (
    !Number.isFinite(visit.yourPrice) ||
    visit.yourPrice <= 0 ||
    !visit.priceDate
  )
    return null;
  const day = visit.estimatedArrival?.slice(0, 10);
  return {
    label: visit.priceEstimated ? 'Estimated price' : 'Arrival price',
    value: `${visit.yourPrice.toFixed(3)} ${visit.currency || ''}/${visit.unit || 'US gal'}`,
    note: visit.priceEstimated
      ? `quoted ${visit.priceDate}`
      : day
        ? `on ${day}`
        : '',
  };
}

export function createFuelVisit(
  visit,
  station,
  discount,
  showCost = true,
  formatDistance = distanceLabel,
) {
  const unit = visit.unit || discount.unit || '';
  const row = node('section', 'fleet-station-popup__visit fleet-fuel-visit');
  row.setAttribute('aria-label', `Fuel stop ${visit.number}`);
  const heading = node('div', 'fleet-fuel-visit__heading');
  const name = node('strong', 'fleet-fuel-visit__name');
  name.append(
    node('span', 'fleet-fuel-visit__number', String(visit.number)),
    node('span', 'fleet-fuel-visit__title', 'Fuel stop'),
  );
  // The card names a thing before it says it, this one included: the number
  // stood on its own in the corner with nothing to say what it measured.
  const distance = node(
    'strong',
    'fleet-fuel-visit__distance',
    Number.isFinite(visit.miles) ? formatDistance(visit.miles) : '',
  );
  const away = node('span', 'fleet-fuel-visit__away');
  if (Number.isFinite(visit.miles)) {
    distance.setAttribute('aria-label', `${formatDistance(visit.miles)} away`);
    away.append(node('span', 'fleet-fuel-visit__label', 'Left'), distance);
  }
  heading.append(name, away);
  row.append(heading);
  if (visit.warning) {
    const warning = node('p', 'fleet-fuel-visit__warning', visit.warning);
    warning.setAttribute('role', 'status');
    row.append(warning);
  }
  if (visit.accessOnly) return row;
  const action = node('div', 'fleet-fuel-visit__action');
  const arrow = node('span', 'fleet-fuel-visit__arrow', '→');
  arrow.setAttribute('aria-hidden', 'true');
  action.append(
    node('span', 'fleet-fuel-visit__label', visit.full ? 'Fill up' : 'Buy'),
    node(
      'strong',
      'fleet-fuel-visit__buy',
      stationQuantity(visit.gallons, station, unit),
    ),
  );
  action.append(arrow);
  const levels = node('div', 'fleet-fuel-visit__levels');
  levels.append(
    gauge('On arrival', visit.arrivalGallons, visit.tankGallons, station, unit),
    action,
    gauge(
      'After fueling',
      visit.departureGallons,
      visit.tankGallons,
      station,
      unit,
    ),
  );
  row.append(levels);
  const price = plannedPrice(visit);
  if (price) {
    const line = node('div', 'fleet-fuel-visit__price');
    line.append(
      node('span', 'fleet-fuel-visit__label', price.label),
      node('strong', 'fleet-fuel-visit__price-value', price.value),
    );
    if (price.note)
      line.append(node('span', 'fleet-fuel-visit__label', price.note));
    row.append(line);
  }
  const costLabel = fuelPurchaseCostLabel(visit.purchaseCostUsd);
  if (showCost && costLabel) {
    const cost = node('div', 'fleet-station-popup__visit-cost');
    cost.append(
      node('span', 'fleet-fuel-visit__label', 'Estimated purchase'),
      node('strong', 'fleet-station-popup__cost-value', costLabel),
    );
    row.append(cost);
  }
  return row;
}
