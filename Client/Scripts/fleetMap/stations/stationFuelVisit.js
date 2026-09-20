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
  // The tank as one bar: what is in it on arrival, and what the stop adds.
  // It is the same device as the bar under "Left" on the truck card, and it
  // replaced two dials with an arrow between them - the last dials on the
  // map, a row of three towers where everything else is a named figure.
  const had = fuelGaugeValue(visit.arrivalGallons, visit.tankGallons);
  const after = fuelGaugeValue(visit.departureGallons, visit.tankGallons);
  const quantity = gallons =>
    stationQuantity(gallons, station, unit, Math.round) || '\u2014';
  const tank = node('div', 'fleet-fuel-visit__tank');
  tank.setAttribute('role', 'img');
  tank.setAttribute(
    'aria-label',
    `Tank: ${had ?? '\u2014'}% on arrival, ${after ?? '\u2014'}% after fueling`,
  );
  const hadBar = node('span', 'fleet-fuel-visit__tank-had');
  hadBar.setAttribute('style', `inline-size:${had ?? 0}%`);
  const addBar = node('span', 'fleet-fuel-visit__tank-add');
  addBar.setAttribute(
    'style',
    `inline-size:${Math.max(0, (after ?? had ?? 0) - (had ?? 0))}%`,
  );
  tank.append(hadBar, addBar);
  row.append(tank);

  const facts = node('dl', 'fleet-fuel-visit__facts');
  const fact = (label, value, note, total = false) => {
    const line = node(
      'div',
      `fleet-fuel-visit__fact${total ? ' fleet-fuel-visit__fact--total' : ''}`,
    );
    const figure = node('dd', 'fleet-fuel-visit__figure');
    figure.append(node('strong', 'fleet-fuel-visit__value', value));
    if (note) figure.append(node('span', 'fleet-fuel-visit__note', note));
    line.append(node('dt', 'fleet-fuel-visit__label', label), figure);
    facts.append(line);
  };
  const level = (percent, gallons) =>
    percent === null
      ? [quantity(gallons), '']
      : [`${percent}%`, `\u00b7 ${quantity(gallons)}`];
  fact('On arrival', ...level(had, visit.arrivalGallons));
  fact(
    visit.full ? 'Fill up' : 'Buy',
    stationQuantity(visit.gallons, station, unit) || '\u2014',
  );
  fact('After fueling', ...level(after, visit.departureGallons));
  // The purchase and the price it is made at are one fact. The price stood
  // as a sentence of its own next to "Your price" saying the same number.
  const price = plannedPrice(visit);
  const costLabel = fuelPurchaseCostLabel(visit.purchaseCostUsd);
  const priceNote = price
    ? `\u00b7 at ${price.value.split(' ')[0]}${price.note ? `, ${price.note}` : ''}`
    : '';
  if (showCost && costLabel) {
    fact('Purchase', costLabel, priceNote, true);
  } else if (price) fact(price.label, price.value, price.note);
  row.append(facts);
  return row;
}
