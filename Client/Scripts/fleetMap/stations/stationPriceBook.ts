import type { MapPoint } from '../contracts.d.ts';
import {
  selectStationPrices,
  comparisonPrice,
  priceStatistics,
  priceColor,
} from './stationPrices.ts';

// One station as the map holds it: which station, where it stands, and the
// price the account reads there today.
export type StationItem = {
  station: {
    id?: string;
    externalId?: string;
    name?: string;
    address?: string;
  };
  position: MapPoint;
  discount: any;
};

export const stationId = (item: StationItem): string =>
  item.station.id || item.station.externalId || '';

// What a station's mark says about its price: the price the map compares
// on, and the colour that price earns against every other price on screen.
export type StationAppearance = { price: number | null; color: string };

/**
 * Every fuel price the map knows today - the full station list when it has
 * been loaded, and the cheaper overview until then - and the reading each
 * station's mark takes from it.
 */
export function createStationPriceBook() {
  let stationData: any[] = [];
  let stationDate = '';
  let priceDate = '';
  let priceOverview: any[] = [];
  let stationsLoaded = false;
  let useIfta = false;
  let items: StationItem[] = [];

  // The prices every station is coloured against. The overview stands in
  // for the full list until it has arrived, or while it is a day behind.
  function priceReference() {
    const reference =
      stationsLoaded && stationDate === priceDate
        ? items
        : priceOverview.map(price => ({
            station: { id: price.id },
            discount: {
              currency: price.currency,
              discountPrice: price.cashPrice,
              priceAfterIfta: price.iftaPrice,
            },
          }));
    return {
      byId: new Map(
        reference.map(
          item => [stationId(item as StationItem), item.discount] as const,
        ),
      ),
      stats: priceStatistics(reference, useIfta),
    };
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

  return {
    items: () => items,
    // One reading of the whole screen, taken once and then asked about each
    // station: the statistics and the palette are the same for all of them.
    look() {
      const reference = priceReference();
      const colors = palette();
      return (id: string): StationAppearance => {
        const discount = reference.byId.get(id);
        const price = discount ? comparisonPrice(discount, useIfta) : null;
        return {
          price,
          color: priceColor(
            price,
            reference.stats.get(discount?.currency),
            colors,
          ),
        };
      };
    },
    setStations(data: any, date: string, ifta: unknown) {
      stationData = Array.isArray(data) ? data : [];
      stationDate = date;
      stationsLoaded = true;
      priceDate = date;
      useIfta = ifta === true;
      items = selectStationPrices(stationData, stationDate, useIfta);
    },
    setOverview(data: any, date: string, ifta: unknown) {
      priceDate = date;
      priceOverview = Array.isArray(data) ? data : [];
      useIfta = ifta === true;
    },
    setIfta(value: unknown) {
      useIfta = value === true;
      items = selectStationPrices(stationData, stationDate, useIfta);
    },
    clear() {
      items = [];
      stationData = [];
      stationDate = '';
      priceDate = '';
      priceOverview = [];
      stationsLoaded = false;
    },
  };
}
