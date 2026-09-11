import { selectStationPrices, comparisonPrice, priceStatistics, priceColor } from './stationPrices.js';
import { createStationPopup } from './stationPopup.js';
import { yieldToBrowser } from '../lifecycle/backgroundWork.js';
import { createDetailsCard } from '../ui/detailsCard.js';
import { coordinates } from '../geometry/coordinates.js';

const stationId = item => item.station.id || item.station.externalId;
export function createStationLayer(map, onOpen = () => {}, pointFactory, popupFactory = createDetailsCard, onEdit = () => {}) {
  const pointLayer = pointFactory(map, id => open(id));
  const entries = new Map();
  let editContext = null;
  const popup = createStationPopup(selection => {
    if (!disposed && editContext) onEdit({ ...editContext, ...selection });
  });
  const popupWindow = popupFactory(map, { onClose: close });
  let items = [];
  let stationData = [];
  let stationDate = '';
  let selectedId = null;
  let editing = null;
  let visible = false;
  let recommended = new Set();
  let quantities = new Map();
  let useIfta = false;
  let renderVersion = 0;
  let disposed = false;
  let neutralColor = palette().unavailable;
  const validId = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
    && value !== '00000000-0000-0000-0000-000000000000';
  function updatePopup(item) {
    popup.update({ ...item, canEdit: !!editContext && validId(item.station.id) });
  }

  function close() {
    const selected = entries.get(selectedId);
    if (selected) updatePoint(selectedId, selected, false);
    pointLayer.redraw();
    popupWindow.hide();
    selectedId = null;
  }

  function open(id) {
    if (disposed) return;
    const entry = entries.get(id);
    if (!entry || !visible && !recommended.has(id) && editing?.stationId !== id || selectedId === id) return;
    close();
    onOpen();
    selectedId = id;
    updatePopup({ ...entry.item, fuel: quantities.get(id) });
    updatePoint(id, entry, true);
    pointLayer.redraw();
    popupWindow.show(popup.element, entry.item.position);
  }

  function updatePoint(id, entry, selected = id === selectedId) {
    pointLayer.setPoint(id, entry.item.position, visible ? entry.color : neutralColor, recommended.has(id), selected,
      quantities.get(id)?.numbers, editing?.stationId === id, comparisonPrice(entry.item.discount, useIfta));
  }

  function clearEditing() {
    if (!editing) return;
    const id = editing.stationId;
    editing = null;
    if (!visible && !recommended.has(id) && selectedId === id) close();
    const entry = entries.get(id);
    if (entry) {
      if (recommended.has(id) || items.some(item => stationId(item) === id)) updatePoint(id, entry);
      else {
        if (selectedId === id) close();
        entries.delete(id);
        pointLayer.removePoint(id);
      }
    }
    pointLayer.redraw();
  }

  function palette() {
    const style = getComputedStyle(document.documentElement);
    return {
      low: style.getPropertyValue('--clr-success-700-rgb').trim(),
      middle: style.getPropertyValue('--clr-warning-500-rgb').trim(),
      high: style.getPropertyValue('--clr-danger-700-rgb').trim(),
      unavailable: style.getPropertyValue('--clr-neutral-500-rgb').trim(),
    };
  }

  async function render() {
    const version = ++renderVersion;
    if (disposed) return;
    const colors = palette();
    neutralColor = colors.unavailable;
    const stats = priceStatistics(items, useIfta);
    const active = new Set();
    let processed = 0;
    const displayed = [...items];
    const priceItems = new Set(items);
    const known = new Set(displayed.map(stationId));
    for (const [id, quantity] of quantities) {
      if (known.has(id)) continue;
      const position = coordinates(quantity.point?.latitude, quantity.point?.longitude);
      if (!position || position.lat === 0 && position.lng === 0) continue;
      displayed.push({ station: { id, name: quantity.name, address: quantity.address }, position,
        discount: { currency: quantity.currency, unit: quantity.unit, discountPrice: quantity.yourPrice,
          priceAfterIfta: null, retailPrice: null, savings: null } });
      known.add(id);
    }
    if (editing && !known.has(editing.stationId)) {
      const fallback = entries.get(editing.stationId)?.item;
      if (fallback) displayed.push(fallback);
    }
    for (const item of displayed) {
      if (++processed % 32 === 0) {
        await yieldToBrowser();
        if (disposed || version !== renderVersion) return;
      }
      const id = stationId(item);
      if (!id) continue;
      if (!priceItems.has(item) && !recommended.has(id) && editing?.stationId !== id) continue;
      if (!visible && !recommended.has(id) && editing?.stationId !== id && !entries.has(id)) continue;
      active.add(id);
      const color = priceColor(comparisonPrice(item.discount, useIfta), stats.get(item.discount.currency), colors);
      let entry = entries.get(id);
      if (!entry) {
        entry = { item, color };
        entries.set(id, entry);
      }
      entry.item = item;
      entry.color = color;
      updatePoint(id, entry);
      if (id === selectedId) {
        updatePopup({ ...item, fuel: quantities.get(id) });
        popupWindow.show(popup.element, item.position);
      }
    }
    for (const [id, entry] of entries) {
      if (active.has(id) || editing?.stationId === id) continue;
      if (id === selectedId) close();
      pointLayer.removePoint(id);
      entries.delete(id);
    }
    pointLayer.redraw();
  }

  return {
    setEditContext(context) {
      if (disposed) return;
      if (context?.truckId !== editContext?.truckId || context?.dispatchId !== editContext?.dispatchId) clearEditing();
      editContext = validId(context?.truckId) && validId(context?.dispatchId) ? context : null;
      const entry = entries.get(selectedId);
      if (entry) updatePopup({ ...entry.item, fuel: quantities.get(selectedId) });
    },
    setEditing(station) {
      if (disposed) return null;
      if (!station) { clearEditing(); return null; }
      if (!validId(station.stationId) || station.truckId !== editContext?.truckId
        || station.dispatchId !== editContext?.dispatchId) return null;
      const known = items.find(item => stationId(item) === station.stationId);
      const location = known?.position ?? coordinates(station.point?.latitude, station.point?.longitude);
      if (!location || location.lat === 0 && location.lng === 0) { clearEditing(); return null; }
      clearEditing();
      editing = station;
      const item = known ?? {
        station: { id: station.stationId, name: station.name, address: station.address }, position: location,
        discount: { currency: station.currency, unit: station.unit, discountPrice: station.yourPrice,
          priceAfterIfta: null, retailPrice: null, savings: null },
      };
      const stats = priceStatistics(items, useIfta);
      const color = priceColor(comparisonPrice(item.discount, useIfta), stats.get(item.discount.currency), palette());
      const entry = { item, color };
      entries.set(station.stationId, entry);
      updatePoint(station.stationId, entry);
      pointLayer.redraw();
      return location;
    },
    closePopup() { if (!disposed) close(); },
    handleMapClick(event) {
      if (disposed) return false;
      const id = pointLayer.hitTest(event?.latLng);
      if (!id) return false;
      open(id);
      return true;
    },
    setStations(data, date, ifta) {
      if (disposed) return;
      stationData = Array.isArray(data) ? data : [];
      stationDate = date;
      useIfta = ifta === true;
      items = selectStationPrices(stationData, stationDate, useIfta);
      return render();
    },
    setIfta(value) {
      if (disposed) return;
      useIfta = value === true;
      items = selectStationPrices(stationData, stationDate, useIfta);
      return render();
    },
    setProgress(miles) {
      if (disposed) return;
      for (const [id, quantity] of quantities) {
        if (quantity.visits) {
          const visits = quantity.visits.filter(visit => !Number.isFinite(visit.routeMile) || visit.routeMile >= miles - .5)
            .map(visit => Number.isFinite(visit.routeMile) ? { ...visit, miles: Math.max(0, visit.routeMile - miles) } : visit);
          const next = visits.length ? { ...visits[0], visits, numbers: visits.map(visit => visit.number).join('/') } : null;
          const entry = entries.get(id);
          if (next) quantities.set(id, next);
          else { quantities.delete(id); recommended.delete(id); }
          if (quantity.numbers !== next?.numbers) {
            if (entry) updatePoint(id, entry);
            pointLayer.redraw();
          }
          if (id === selectedId && entry) updatePopup({ ...entry.item, fuel: next });
          continue;
        }
        if (!Number.isFinite(quantity.routeMile)) continue;
        if (quantity.routeMile < miles - .5) {
          quantities.delete(id);
          recommended.delete(id);
          const entry = entries.get(id);
          if (entry) updatePoint(id, entry);
          pointLayer.redraw();
          if (id === selectedId && entry) updatePopup(entry.item);
          continue;
        }
        const ahead = Math.max(0, quantity.routeMile - miles);
        if (Math.round(ahead) === Math.round(quantity.miles) && Math.round(ahead * 1.609344) === Math.round(quantity.miles * 1.609344)) continue;
        quantity.miles = ahead;
        if (id === selectedId) {
          const item = entries.get(id)?.item;
          if (item) updatePopup({ ...item, fuel: quantity });
        }
      }
    },
    setRecommended(ids) {
      if (disposed) return;
      recommended = new Set(ids.map(x => typeof x === 'string' ? x : x.id));
      quantities = new Map(ids.filter(x => typeof x !== 'string').map(x => [x.id, x]));
      if (!visible && !recommended.has(selectedId) && editing?.stationId !== selectedId) close();
      return render();
    },
    setVisible(value) {
      if (disposed) return;
      if (visible === (value === true)) return;
      visible = value === true;
      pointLayer.setVisible(visible);
      if (!visible && !recommended.has(selectedId) && editing?.stationId !== selectedId) close();
      return render();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      editContext = null;
      editing = null;
      renderVersion++;
      close();
      popupWindow.dispose();
      popup.dispose();
      for (const id of entries.keys()) {
        pointLayer.removePoint(id);
      }
      entries.clear();
      items = [];
      stationData = [];
      stationDate = '';
      quantities.clear();
      recommended.clear();
      pointLayer.dispose();
    },
  };
}
