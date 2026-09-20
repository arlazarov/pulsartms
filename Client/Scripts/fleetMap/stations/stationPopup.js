import { stationPurchase } from './stationQuantity.js';
import { distanceLabel } from '../ui/distanceLabel.js';
import { addressLines } from '../ui/addressLines.js';
import { createFuelVisit } from './stationFuelVisit.js';
import { createPriceComparison } from './stationPriceComparison.js';
export function createStationPopup(
  onEdit = () => {},
  formatDistance = distanceLabel,
) {
  const element = document.createElement('div');
  element.className = 'fleet-station-popup';
  const title = document.createElement('strong');
  title.className = 'fleet-station-popup__title';
  const planLabel = document.createElement('span');
  planLabel.className = 'fleet-station-popup__plan-label';
  planLabel.hidden = true;
  const address = document.createElement('button');
  address.type = 'button';
  address.className = 'fleet-station-popup__address';
  address.title = 'Copy address';
  const street = document.createElement('span');
  street.className = 'fleet-station-popup__address-line';
  const locality = document.createElement('span');
  locality.className = 'fleet-station-popup__address-line';
  locality.hidden = true;
  address.append(street, locality);
  const copyStatus = document.createElement('span');
  copyStatus.className = 'fleet-station-popup__copy-status';
  copyStatus.setAttribute('role', 'status');
  let addressText = '';
  let copyVersion = 0;
  async function copyAddress(event) {
    event.stopPropagation();
    const version = ++copyVersion;
    try {
      await navigator.clipboard.writeText(addressText);
      if (version === copyVersion) copyStatus.textContent = 'Copied';
    } catch {
      if (version === copyVersion)
        copyStatus.textContent = 'Could not copy. Try again.';
    }
  }
  address.addEventListener('click', copyAddress);
  const prices = document.createElement('dl');
  prices.className = 'fleet-station-popup__prices';
  const priceUnit = document.createElement('dt');
  priceUnit.className = 'fleet-station-popup__price-unit';
  priceUnit.hidden = true;
  prices.append(priceUnit);
  const fields = {};
  const terms = {};
  const comparison = document.createElement('div');
  comparison.className = 'fleet-station-popup__comparison';
  comparison.hidden = true;
  let comparisonKey = '';
  for (const [name, label] of Object.entries({
    retail: 'Retail price',
    discount: 'Your price',
    ifta: 'Price after IFTA',
    savings: 'Savings',
  })) {
    const term = document.createElement('dt');
    term.textContent = label;
    term.className = `fleet-station-popup__${name}-label`;
    const value = document.createElement('dd');
    value.className = `fleet-station-popup__${name}`;
    fields[name] = value;
    terms[name] = term;
    prices.append(term, value);
  }
  const distance = document.createElement('p');
  distance.hidden = true;
  const purchase = document.createElement('strong');
  purchase.className = 'fleet-station-popup__purchase';
  purchase.hidden = true;
  const visits = document.createElement('div');
  visits.className = 'fleet-station-popup__visits';
  visits.hidden = true;
  let visitsKey = '[]';
  let editSelection = null;
  const actions = document.createElement('div');
  actions.className = 'fleet-station-popup__actions';
  actions.hidden = true;
  const edit = document.createElement('button');
  edit.type = 'button';
  edit.className = 'btn btn--small';
  const addVisit = document.createElement('button');
  addVisit.type = 'button';
  addVisit.className = 'btn btn--small';
  addVisit.textContent = 'Add another visit';
  addVisit.hidden = true;
  function editClick(event) {
    event.stopPropagation();
    if (editSelection) onEdit(editSelection);
  }
  function addClick(event) {
    event.stopPropagation();
    if (editSelection)
      onEdit({ ...editSelection, beforeStopId: null, addNew: true });
  }
  edit.addEventListener('click', editClick);
  addVisit.addEventListener('click', addClick);
  actions.append(edit, addVisit);
  // The card is two halves, and they are two elements because they are two
  // halves on the screen: the place - its name, where it is, what fuel costs
  // there and what the price did - and the plan for it, which ends with the
  // buttons that change the plan. Laid out flat, those buttons could only
  // stand across the foot of both halves, leaving the corner above them
  // empty. An ordinary quote has no plan, so the halves dissolve and the
  // card is the single column it has always been.
  const place = document.createElement('div');
  place.className = 'fleet-station-popup__place';
  place.append(title, address, copyStatus, prices, comparison);
  const planHead = document.createElement('div');
  planHead.className = 'fleet-station-popup__plan-head';
  planHead.append(planLabel, distance);
  const plan = document.createElement('div');
  plan.className = 'fleet-station-popup__plan';
  plan.append(planHead, visits, purchase, actions);
  element.append(place, plan);

  function format(value) {
    return value == null || !Number.isFinite(Number(value))
      ? 'N/A'
      : Number(value).toFixed(3);
  }
  function set(node, property, value) {
    if (node[property] !== value) node[property] = value;
  }

  return {
    element,
    update({ station, discount, fuel, canEdit = false }) {
      const plannedVisits = fuel?.visits ?? [];
      canEdit &&= !plannedVisits.some(visit => visit.accessOnly);
      const singleVisit = plannedVisits.length === 1;
      set(
        element,
        'className',
        `fleet-station-popup${plannedVisits.length ? ' fleet-station-popup--planned' : ''}${singleVisit ? ' fleet-station-popup--single' : ''}`,
      );
      set(planLabel, 'hidden', plannedVisits.length === 0);
      set(
        planLabel,
        'textContent',
        plannedVisits.length
          ? `Fuel ${singleVisit ? 'stop' : 'stops'} ${plannedVisits.map(visit => visit.number).join(', ')}`
          : '',
      );
      editSelection = canEdit
        ? {
            stationId: station.id,
            name: station.name || '',
            beforeStopId: plannedVisits[0]?.beforeStopId ?? null,
            addNew: plannedVisits.length === 0,
          }
        : null;
      // The purchase is a fact of the visit now, beside the fill it pays
      // for, so down here there are only the actions - outlined, the way
      // the truck card's are. A filled button is a shape no other card on
      // the map uses.
      set(actions, 'hidden', !canEdit);
      set(edit, 'hidden', !canEdit);
      set(edit, 'className', 'btn btn--small');
      set(
        edit,
        'textContent',
        plannedVisits.length ? 'Edit fuel plan' : 'Add to fuel plan',
      );
      set(addVisit, 'hidden', !canEdit || plannedVisits.length === 0);
      const key = JSON.stringify(
        plannedVisits.map(visit => [
          visit.number,
          visit.warning,
          visit.accessOnly,
          visit.gallons,
          visit.full,
          visit.arrivalGallons,
          visit.departureGallons,
          visit.tankGallons,
          visit.purchaseCostUsd,
          visit.yourPrice,
          visit.priceDate,
          visit.estimatedArrival,
          visit.priceEstimated,
          visit.unit,
          discount.unit,
          station.country,
          Number.isFinite(visit.miles) ? formatDistance(visit.miles) : '',
        ]),
      );
      if (key !== visitsKey) {
        visitsKey = key;
        const rows = plannedVisits.map(visit =>
          createFuelVisit(visit, station, discount, true, formatDistance),
        );
        visits.replaceChildren(...rows);
      }
      set(visits, 'hidden', plannedVisits.length === 0);
      // One visit: what is left to it is said in the head of the card,
      // where the truck card says its own. Several visits each say theirs.
      const headMiles = singleVisit ? plannedVisits[0].miles : fuel?.miles;
      set(
        distance,
        'hidden',
        plannedVisits.length > 1 || !Number.isFinite(headMiles),
      );
      set(
        distance,
        'className',
        `fleet-station-popup__distance${singleVisit ? ' fleet-station-popup__distance--left' : ''}`,
      );
      set(
        distance,
        'textContent',
        distance.hidden
          ? ''
          : singleVisit
            ? formatDistance(headMiles)
            : `${formatDistance(headMiles)} away`,
      );
      set(purchase, 'hidden', plannedVisits.length > 0 || !fuel);
      set(
        purchase,
        'textContent',
        purchase.hidden
          ? ''
          : stationPurchase(
              { ...fuel, unit: fuel?.unit || discount.unit },
              station,
            ),
      );
      set(title, 'textContent', station.name || '');
      const nextAddress = station.address || '';
      if (nextAddress !== addressText) {
        copyVersion++;
        copyStatus.textContent = '';
        addressText = nextAddress;
        const lines = addressLines(addressText);
        set(street, 'textContent', lines.street);
        set(locality, 'textContent', lines.locality);
        set(locality, 'hidden', !lines.locality);
      }
      set(address, 'disabled', !addressText);
      set(
        priceUnit,
        'textContent',
        [discount.currency, discount.unit].filter(Boolean).join(' / '),
      );
      set(priceUnit, 'hidden', !priceUnit.textContent);
      set(fields.retail, 'textContent', format(discount.retailPrice));
      set(fields.discount, 'textContent', format(discount.discountPrice));
      set(fields.ifta, 'textContent', format(discount.priceAfterIfta));
      // A price after IFTA that does not exist is not a row reading "N/A":
      // it said nothing, in the colour of a saving, on most of the map.
      const noIfta =
        discount.priceAfterIfta == null ||
        !Number.isFinite(Number(discount.priceAfterIfta));
      set(terms.ifta, 'hidden', noIfta);
      set(fields.ifta, 'hidden', noIfta);
      set(
        prices,
        'className',
        `fleet-station-popup__prices${noIfta ? ' fleet-station-popup__prices--no-ifta' : ''}`,
      );
      set(fields.savings, 'textContent', format(discount.savings));
      const nextComparisonKey = JSON.stringify([discount, discount.comparison]);
      if (nextComparisonKey !== comparisonKey) {
        comparisonKey = nextComparisonKey;
        const table = createPriceComparison(discount);
        if (table || comparison.children.length)
          comparison.replaceChildren(...(table ? [table] : []));
        // The price list stays: the days are a line under it, not a table
        // in place of it.
        set(comparison, 'hidden', !table);
      }
    },
    dispose() {
      copyVersion++;
      address.removeEventListener('click', copyAddress);
      edit.removeEventListener('click', editClick);
      addVisit.removeEventListener('click', addClick);
      editSelection = null;
      element.remove();
    },
  };
}
