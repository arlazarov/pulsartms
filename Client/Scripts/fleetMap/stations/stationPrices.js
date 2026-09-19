import { coordinates } from '../geometry/coordinates.js';

const unavailableDiscount = Object.freeze({
  currency: '',
  unit: '',
  product: 'Diesel',
  retailPrice: null,
  discountPrice: null,
  priceAfterIfta: null,
  savings: null,
});

export function selectStationPrices(stations, date, useIfta = false) {
  return (Array.isArray(stations) ? stations : []).flatMap(station => {
    const position = coordinates(station.latitude, station.longitude);
    if (!position) return [];
    const active = value =>
      value && date >= value.effectiveFrom && date <= value.effectiveTo;
    const selected = useIfta
      ? (station.iftaDiscount ?? station.cashDiscount)
      : station.cashDiscount;
    if (!(station.discounts || []).some(active) && !active(selected)) return [];
    const comparison = useIfta
      ? station.iftaComparison
      : station.cashComparison;
    return [
      {
        station,
        discount: active(selected)
          ? comparison?.date === date
            ? { ...selected, comparison }
            : selected
          : unavailableDiscount,
        position,
      },
    ];
  });
}

export function comparisonPrice(discount, useIfta) {
  const value = useIfta ? discount.priceAfterIfta : discount.discountPrice;
  return value != null && Number.isFinite(Number(value)) && Number(value) > 0
    ? Number(value)
    : null;
}

export function priceStatistics(items, useIfta) {
  const groups = new Map();
  for (const { discount } of items) {
    const price = comparisonPrice(discount, useIfta);
    if (price === null) continue;
    const key = discount.currency;
    const stats = groups.get(key) || {
      min: price,
      max: price,
      total: 0,
      count: 0,
      prices: [],
    };
    stats.min = Math.min(stats.min, price);
    stats.max = Math.max(stats.max, price);
    stats.total += price;
    stats.count++;
    stats.prices.push(price);
    groups.set(key, stats);
  }
  for (const stats of groups.values()) {
    stats.average = stats.total / stats.count;
    stats.prices.sort((a, b) => a - b);
    stats.priceTiers = [
      ...new Set(stats.prices.map(value => Math.round(value * 100))),
    ];
    stats.pricePositions = pricePositions(stats.priceTiers);
    const middle = Math.floor(stats.prices.length / 2);
    stats.median =
      stats.prices.length % 2
        ? stats.prices[middle]
        : (stats.prices[middle - 1] + stats.prices[middle]) / 2;
  }
  return groups;
}

export function priceColor(price, stats, palette) {
  if (price === null || !stats) return palette.unavailable;
  if (stats.min === stats.max) return palette.middle;
  // Use a global nonlinear price axis: real price gaps remain visible, while
  // sublinear weighting prevents a distant outlier from flattening the rest.
  const tier = Math.round(Number(price) * 100);
  const percentile = stats.pricePositions?.get(tier) ?? 0.5;
  if (percentile <= 0) return palette.low;
  if (percentile >= 1) return palette.high;
  return percentile <= 0.5
    ? interpolateHsl(palette.low, palette.middle, percentile * 2)
    : interpolateHsl(palette.middle, palette.high, (percentile - 0.5) * 2);
}

function pricePositions(tiers) {
  const positions = new Map();
  if (tiers.length === 0) return positions;
  if (tiers.length === 1) {
    positions.set(tiers[0], 0.5);
    return positions;
  }
  const cumulative = [0];
  for (let index = 1; index < tiers.length; index++) {
    cumulative.push(
      cumulative[index - 1] +
        Math.pow(Math.max(1, tiers[index] - tiers[index - 1]), 0.2),
    );
  }
  const total = cumulative.at(-1);
  tiers.forEach((tier, index) =>
    positions.set(tier, cumulative[index] / total),
  );
  return positions;
}

function interpolateHsl(from, to, ratio) {
  const start = rgbToHsl(from.split(',').map(Number));
  const end = rgbToHsl(to.split(',').map(Number));
  const amount = Math.max(0, Math.min(1, ratio));
  const hue = start[0] + (end[0] - start[0]) * amount;
  return hslToRgb(
    hue,
    start[1] + (end[1] - start[1]) * amount,
    start[2] + (end[2] - start[2]) * amount,
  ).join(',');
}

function rgbToHsl([red, green, blue]) {
  const [r, g, b] = [red / 255, green / 255, blue / 255];
  const max = Math.max(r, g, b),
    min = Math.min(r, g, b);
  const lightness = (max + min) / 2;
  if (max === min) return [0, 0, lightness];
  const delta = max - min;
  const saturation = delta / (1 - Math.abs(2 * lightness - 1));
  const hue =
    max === r
      ? 60 * (((g - b) / delta) % 6)
      : max === g
        ? 60 * ((b - r) / delta + 2)
        : 60 * ((r - g) / delta + 4);
  return [hue < 0 ? hue + 360 : hue, saturation, lightness];
}

function hslToRgb(hue, saturation, lightness) {
  const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation;
  const section = hue / 60;
  const x = chroma * (1 - Math.abs((section % 2) - 1));
  const [r, g, b] =
    section < 1
      ? [chroma, x, 0]
      : section < 2
        ? [x, chroma, 0]
        : section < 3
          ? [0, chroma, x]
          : section < 4
            ? [0, x, chroma]
            : section < 5
              ? [x, 0, chroma]
              : [chroma, 0, x];
  const match = lightness - chroma / 2;
  return [r, g, b].map(value => Math.round((value + match) * 255));
}
