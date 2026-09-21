import { stationQuantity } from './stationQuantity.ts';
import { distanceLabel } from '../ui/distanceLabel.ts';

// One planned visit to a pump: which stop of the plan it is, what the tank
// does there, and what the fill costs.
export type FuelVisit = {
  number: number | string;
  miles?: number;
  warning?: string;
  accessOnly?: boolean;
  gallons?: number;
  arrivalGallons?: number;
  departureGallons?: number;
  tankGallons?: number;
  purchaseCostUsd?: number;
  yourPrice?: number;
  priceDate?: string;
  priceEstimated?: boolean;
  estimatedArrival?: string;
  currency?: string;
  unit?: string;
  full?: boolean;
};

function node(tag: string, className: string, text?: string) {
  const element = document.createElement(tag);
  element.className = className;
  if (text !== undefined) element.textContent = text;
  return element;
}

export function fuelGaugeValue(
  gallons: number | undefined,
  tankGallons: number | undefined,
): number | null {
  if (
    !Number.isFinite(gallons) ||
    !Number.isFinite(tankGallons) ||
    gallons! < 0 ||
    tankGallons! <= 0 ||
    gallons! > tankGallons!
  )
    return null;
  return Math.round((gallons! / tankGallons!) * 100);
}

export function fuelPurchaseCostLabel(costUsd: number | undefined): string {
  return Number.isFinite(costUsd) && costUsd! >= 0
    ? `≈ $${costUsd!.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} USD`
    : '';
}

// A named figure, the way every other fact on these cards is written. This
// was one sentence about itself - "Estimated price: 6.112 USD/US gal · quote
// 2026-09-20" - which is a third way of saying a labelled number.
export function plannedPrice(
  visit: FuelVisit,
): { label: string; value: string; note: string } | null {
  if (
    !Number.isFinite(visit.yourPrice) ||
    visit.yourPrice! <= 0 ||
    !visit.priceDate
  )
    return null;
  const day = visit.estimatedArrival?.slice(0, 10);
  return {
    label: visit.priceEstimated ? 'Estimated price' : 'Arrival price',
    value: `${visit.yourPrice!.toFixed(3)} ${visit.currency || ''}/${visit.unit || 'US gal'}`,
    note: visit.priceEstimated
      ? `quoted ${visit.priceDate}`
      : day
        ? `on ${day}`
        : '',
  };
}

export function createFuelVisit(
  visit: FuelVisit,
  station: { country?: string },
  discount: { unit?: string },
  showCost = true,
  formatDistance: (miles: number) => string = distanceLabel,
): HTMLElement {
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
    Number.isFinite(visit.miles) ? formatDistance(visit.miles!) : '',
  );
  const away = node('span', 'fleet-fuel-visit__away');
  if (Number.isFinite(visit.miles)) {
    distance.setAttribute('aria-label', `${formatDistance(visit.miles!)} away`);
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
  // The tank, said before it is drawn: "Tank 63% -> 100%", what the stop
  // adds at the far end, the bar under those words, and the gallons under
  // the two ends of it. A bare bar stood here directly under "Left 42 mi",
  // where the truck card draws how much of the road is behind - so it read
  // as the way to the pump. Before that it was two dials and an arrow.
  const had = fuelGaugeValue(visit.arrivalGallons, visit.tankGallons);
  const after = fuelGaugeValue(visit.departureGallons, visit.tankGallons);
  const quantity = (gallons: number | undefined) =>
    stationQuantity(gallons!, station, unit, Math.round) || '—';
  const known = had !== null && after !== null;
  const tank = node('div', 'fleet-fuel-visit__tank');
  const levels = node('div', 'fleet-fuel-visit__levels');
  const arrow = node('span', 'fleet-fuel-visit__arrow', '\u2192');
  arrow.setAttribute('aria-hidden', 'true');
  const added = stationQuantity(visit.gallons!, station, unit);
  levels.append(
    node('span', 'fleet-fuel-visit__label', 'Tank'),
    node(
      'strong',
      'fleet-fuel-visit__level',
      known ? `${had}%` : quantity(visit.arrivalGallons),
    ),
    arrow,
    node(
      'strong',
      'fleet-fuel-visit__level',
      known ? `${after}%` : quantity(visit.departureGallons),
    ),
    node('span', 'fleet-fuel-visit__added', added ? `+ ${added}` : ''),
  );
  tank.append(levels);
  // Without the size of the tank there is no scale to draw the bar on, and
  // the gallons are already the two figures above.
  if (known) {
    const bar = node('div', 'fleet-fuel-visit__bar');
    bar.setAttribute('role', 'img');
    bar.setAttribute(
      'aria-label',
      `Tank: ${had}% on arrival, ${after}% after fueling`,
    );
    const hadBar = node('span', 'fleet-fuel-visit__bar-had');
    hadBar.setAttribute('style', `inline-size:${had}%`);
    const addBar = node('span', 'fleet-fuel-visit__bar-add');
    addBar.setAttribute('style', `inline-size:${Math.max(0, after - had)}%`);
    bar.append(hadBar, addBar);
    const ends = node('div', 'fleet-fuel-visit__ends');
    ends.append(
      node('span', '', `${quantity(visit.arrivalGallons)} on arrival`),
      node('span', '', `${quantity(visit.departureGallons)} after`),
    );
    tank.append(bar, ends);
  }
  row.append(tank);

  const facts = node('dl', 'fleet-fuel-visit__facts');
  const fact = (label: string, value: string, note?: string) => {
    const line = node('div', 'fleet-fuel-visit__fact');
    const figure = node('dd', 'fleet-fuel-visit__figure');
    figure.append(node('strong', 'fleet-fuel-visit__value', value));
    if (note) figure.append(node('span', 'fleet-fuel-visit__note', note));
    line.append(node('dt', 'fleet-fuel-visit__label', label), figure);
    facts.append(line);
  };
  // What the fill comes to, and nothing after it: the price it is made at
  // is the figure the other half of the card is built around, and the day
  // it was quoted on is the day the whole card is showing. Said here as
  // well, it read as a second, smaller total.
  //
  // Where the total cannot be worked out, the price takes its place - a
  // fact that is otherwise missing from a visit on a card that is not the
  // planned one.
  const costLabel = fuelPurchaseCostLabel(visit.purchaseCostUsd);
  const price = showCost && costLabel ? null : plannedPrice(visit);
  if (price) fact(price.label, price.value, price.note);
  else if (showCost && costLabel) fact('Purchase', costLabel);
  if (facts.children.length) row.append(facts);
  return row;
}
