import type { MapPoint, RoutePoint } from '../contracts.d.ts';
import type { StationEdit } from './stationPopup.ts';
import type { StationItem } from './stationPriceBook.ts';
import type { PlannedFuel } from './stationPlan.ts';
import { createStationPopup } from './stationPopup.ts';
import { createStationPriceBook, stationId } from './stationPriceBook.ts';
import { createStationPlan } from './stationPlan.ts';
import { fuelVisitLabel } from './stationQuantity.ts';
import { yieldToBrowser } from '../lifecycle/backgroundWork.ts';
import { createDetailsCard } from '../ui/detailsCard.ts';
import { coordinates } from '../geometry/coordinates.ts';

// What this layer asks of whatever draws the station marks.
export type StationPointLayer = {
  setPoint(
    id: string,
    position: MapPoint,
    color: string,
    recommended: boolean,
    selected: boolean,
    label: string | undefined,
    editing: boolean,
    price: number | null,
  ): void;
  removePoint(id: string): void;
  redraw(): void;
  setVisible(visible: boolean): void;
  hitTest(latLng: unknown): string | null;
  dispose(): void;
};

// The truck and the dispatch a fuel plan is being edited for.
export type StationEditContext = { truckId?: string; dispatchId?: string };

// The station the plan editor is standing on, as the page describes it.
export type EditingStation = StationEditContext & {
  stationId: string;
  name?: string;
  address?: string;
  point?: RoutePoint;
  currency?: string;
  unit?: string;
  yourPrice?: number;
};

type Entry = { item: StationItem; price: number | null; color: string };

/**
 * Every fuel station on the map, and the card one opens. What each costs is
 * in the price book, and what the driver is meant to buy is in the plan;
 * this layer decides which stations are on screen and which one is open.
 *
 * @param onOpen
 *   A station's card is opening; the page closes whatever else was open.
 * @param onEdit
 *   The station the plan editor should open on. The truck and the dispatch
 *   come from the context this layer is holding, the rest from the card.
 */
export function createStationLayer(
  map: google.maps.Map,
  onOpen: () => void = () => {},
  pointFactory: (
    map: google.maps.Map,
    onSelect: (id: string) => void,
  ) => StationPointLayer = () => {
    throw new Error('The station layer was mounted without a point layer.');
  },
  popupFactory: (
    map: google.maps.Map,
    options: { onClose: () => void },
  ) => {
    show(content: Element, position?: unknown): void;
    hide(): void;
    dispose(): void;
  } = createDetailsCard,
  onEdit: (selection: StationEditContext & StationEdit) => void = () => {},
  formatDistance?: (miles: number) => string,
) {
  const pointLayer = pointFactory(map, id => open(id));
  const entries = new Map<string, Entry>();
  const prices = createStationPriceBook();
  const plan = createStationPlan();
  let editContext: StationEditContext | null = null;
  const popup = createStationPopup(selection => {
    if (!disposed && editContext) onEdit({ ...editContext, ...selection });
  }, formatDistance);
  const popupWindow = popupFactory(map, { onClose: close });
  let selectedId: string | null = null;
  let editing: EditingStation | null = null;
  let visible = false;
  let renderVersion = 0;
  let disposed = false;
  const validId = (value: unknown) =>
    typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
      value,
    ) &&
    value !== '00000000-0000-0000-0000-000000000000';

  function updatePopup(item: StationItem & { fuel?: PlannedFuel | null }) {
    popup.update({
      ...item,
      canEdit: !!editContext && validId(item.station.id),
    });
  }

  function showCard(id: string, item: StationItem) {
    updatePopup({ ...item, fuel: plan.fuelAt(id) });
  }

  function close() {
    const selected = selectedId === null ? undefined : entries.get(selectedId);
    if (selected) updatePoint(selectedId!, selected, false);
    pointLayer.redraw();
    popupWindow.hide();
    selectedId = null;
  }

  function open(id: string) {
    if (disposed) return;
    const entry = entries.get(id);
    if (
      !entry ||
      (!visible && !plan.recommends(id) && editing?.stationId !== id) ||
      selectedId === id
    )
      return;
    close();
    onOpen();
    selectedId = id;
    showCard(id, entry.item);
    updatePoint(id, entry, true);
    pointLayer.redraw();
    popupWindow.show(popup.element, entry.item.position);
  }

  function updatePoint(id: string, entry: Entry, selected = id === selectedId) {
    pointLayer.setPoint(
      id,
      entry.item.position,
      entry.color,
      plan.recommends(id),
      selected,
      fuelVisitLabel(plan.fuelAt(id)),
      editing?.stationId === id,
      entry.price,
    );
  }

  function clearEditing() {
    if (!editing) return;
    const id = editing.stationId;
    editing = null;
    if (!visible && !plan.recommends(id) && selectedId === id) close();
    const entry = entries.get(id);
    if (entry) {
      if (
        plan.recommends(id) ||
        prices.items().some(item => stationId(item) === id)
      )
        updatePoint(id, entry);
      else {
        if (selectedId === id) close();
        entries.delete(id);
        pointLayer.removePoint(id);
      }
    }
    pointLayer.redraw();
  }

  // A station the plan buys fuel at is drawn even when the station list is
  // switched off, so the page always sees where the driver is meant to stop.
  function plannedStations(known: Set<string>) {
    const extra: StationItem[] = [];
    for (const [id, quantity] of plan.stations()) {
      if (known.has(id)) continue;
      const position = coordinates(
        quantity.point?.latitude,
        quantity.point?.longitude,
      );
      if (!position || (position.lat === 0 && position.lng === 0)) continue;
      extra.push({
        station: { id, name: quantity.name, address: quantity.address },
        position,
        discount: {
          currency: quantity.currency,
          unit: quantity.unit,
          discountPrice: quantity.yourPrice,
          priceAfterIfta: null,
          retailPrice: null,
          savings: null,
        },
      });
      known.add(id);
    }
    return extra;
  }

  async function render() {
    const version = ++renderVersion;
    if (disposed) return;
    const active = new Set<string>();
    let processed = 0;
    const items = prices.items();
    const displayed = [...items];
    const priceItems = new Set(items);
    const known = new Set(displayed.map(stationId));
    displayed.push(...plannedStations(known));
    if (editing && !known.has(editing.stationId)) {
      const fallback = entries.get(editing.stationId)?.item;
      if (fallback) displayed.push(fallback);
    }
    const appearanceOf = prices.look();
    for (const item of displayed) {
      if (++processed % 32 === 0) {
        await yieldToBrowser();
        if (disposed || version !== renderVersion) return;
      }
      const id = stationId(item);
      if (!id) continue;
      if (
        !priceItems.has(item) &&
        !plan.recommends(id) &&
        editing?.stationId !== id
      )
        continue;
      if (
        !visible &&
        !plan.recommends(id) &&
        editing?.stationId !== id &&
        !entries.has(id)
      )
        continue;
      active.add(id);
      const appearance = appearanceOf(id);
      let entry = entries.get(id);
      if (!entry) {
        entry = { item, ...appearance };
        entries.set(id, entry);
      }
      entry.item = item;
      Object.assign(entry, appearance);
      updatePoint(id, entry);
      if (id === selectedId) {
        showCard(id, item);
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

  function refreshCard() {
    const entry = selectedId === null ? undefined : entries.get(selectedId);
    if (entry) showCard(selectedId!, entry.item);
  }

  return {
    refreshDistances() {
      if (!disposed) refreshCard();
    },
    setEditContext(context: StationEditContext | null) {
      if (disposed) return;
      if (
        context?.truckId !== editContext?.truckId ||
        context?.dispatchId !== editContext?.dispatchId
      )
        clearEditing();
      editContext =
        validId(context?.truckId) && validId(context?.dispatchId)
          ? context
          : null;
      refreshCard();
    },
    setEditing(station: EditingStation | null) {
      if (disposed) return null;
      if (!station) {
        clearEditing();
        return null;
      }
      if (
        !validId(station.stationId) ||
        station.truckId !== editContext?.truckId ||
        station.dispatchId !== editContext?.dispatchId
      )
        return null;
      const known = prices
        .items()
        .find(item => stationId(item) === station.stationId);
      const location =
        known?.position ??
        coordinates(station.point?.latitude, station.point?.longitude);
      if (!location || (location.lat === 0 && location.lng === 0)) {
        clearEditing();
        return null;
      }
      clearEditing();
      editing = station;
      const item: StationItem = known ?? {
        station: {
          id: station.stationId,
          name: station.name,
          address: station.address,
        },
        position: location,
        discount: {
          currency: station.currency,
          unit: station.unit,
          discountPrice: station.yourPrice,
          priceAfterIfta: null,
          retailPrice: null,
          savings: null,
        },
      };
      const entry = { item, ...prices.look()(station.stationId) };
      entries.set(station.stationId, entry);
      updatePoint(station.stationId, entry);
      pointLayer.redraw();
      return location;
    },
    closePopup() {
      if (!disposed) close();
    },
    handleMapClick(event: { latLng?: unknown } | undefined) {
      if (disposed) return false;
      const id = pointLayer.hitTest(event?.latLng);
      if (!id) return false;
      open(id);
      return true;
    },
    setStations(data: unknown, date: string, ifta: unknown) {
      if (disposed) return;
      prices.setStations(data, date, ifta);
      return render();
    },
    setPriceOverview(data: unknown, date: string, ifta: unknown) {
      if (disposed) return;
      prices.setOverview(data, date, ifta);
      return render();
    },
    setIfta(value: unknown) {
      if (disposed) return;
      prices.setIfta(value);
      return render();
    },
    setProgress(miles: number) {
      if (disposed) return;
      for (const change of plan.advance(miles)) {
        const entry = entries.get(change.id);
        if (change.mark) {
          if (entry) updatePoint(change.id, entry);
          pointLayer.redraw();
        }
        if (change.id === selectedId && entry)
          updatePopup({ ...entry.item, fuel: change.fuel });
      }
    },
    setRecommended(ids: Parameters<typeof plan.set>[0]) {
      if (disposed) return;
      plan.set(ids);
      if (
        !visible &&
        !plan.recommends(selectedId) &&
        editing?.stationId !== selectedId
      )
        close();
      return render();
    },
    setVisible(value: unknown) {
      if (disposed) return;
      if (visible === (value === true)) return;
      visible = value === true;
      pointLayer.setVisible(visible);
      if (
        !visible &&
        !plan.recommends(selectedId) &&
        editing?.stationId !== selectedId
      )
        close();
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
      prices.clear();
      plan.clear();
      pointLayer.dispose();
    },
  };
}
