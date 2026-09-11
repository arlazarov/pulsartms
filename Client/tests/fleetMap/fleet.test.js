import test from 'node:test';
import assert from 'node:assert/strict';
import { coordinates } from '../../Scripts/fleetMap/geometry/coordinates.js';
import { createTruckPoint, mergeTruckPoints } from '../../Scripts/fleetMap/trucks/truckPoints.js';
import { advancePlaybackTime, getTruckPosition, truckPlaybackDelay } from '../../Scripts/fleetMap/trucks/truckPlayback.js';
import { selectStationPrices, comparisonPrice, priceStatistics, priceColor } from '../../Scripts/fleetMap/stations/stationPrices.js';

test('coordinates reject invalid values and preserve zero', () => {
  assert.deepEqual(coordinates(0, 0), { lat: 0, lng: 0 });
  for (const pair of [[null, 0], [91, 0], [0, 181], ['invalid', 0]]) assert.equal(coordinates(...pair), null);
  assert.equal(createTruckPoint({ latitude: 0, longitude: 0, updatedAt: 'bad' }), null);
});

test('playback crosses north by the shortest heading and interpolates speed', () => {
  const points = [{ latitude: 0, longitude: 0, gpsTime: 1000, heading: 350, speed: 10 },
    { latitude: 2, longitude: 4, gpsTime: 2000, heading: 10, speed: 20 }];
  const position = getTruckPosition(points, 1500);
  assert.equal(position.heading, 0);
  assert.equal(position.speed, 15);
  assert.equal(position.latitude, 1);
  assert.equal(getTruckPosition(points, 3000), points[1]);
});

test('playback keeps a full polling buffer and resumes without flying after an outage', () => {
  assert.equal(truckPlaybackDelay, 75000);
  assert.equal(advancePlaybackTime(undefined, 90000, 100000, 0), 90000);
  assert.equal(advancePlaybackTime(90000, 90000, 110000, 30000), 90000);
  assert.equal(advancePlaybackTime(90000, 130000, 110000, 100), 90110);
  assert.equal(advancePlaybackTime(90000, 130000, 110000, 10000), 90275);
});

test('duplicate telemetry timestamps are replaced and memory stays bounded', () => {
  const points = Array.from({ length: 500 }, (_, n) => ({ gpsTime: n, latitude: n }));
  const merged = mergeTruckPoints(points, [{ gpsTime: 499, latitude: 7 }], null, 0);
  assert.equal(merged.length, 300);
  assert.equal(merged.at(-1).latitude, 7);
});

test('station selection respects date and coordinate validity', () => {
  const discounts = [{ effectiveFrom: '2026-09-05', effectiveTo: '2026-09-05', currency: 'CAD', unit: 'L', product: 'Diesel', discountPrice: 2 }];
  const stations = [0, 100].map(latitude => ({latitude, longitude: 0, discounts, cashDiscount: discounts[0], iftaDiscount: null}));
  assert.equal(selectStationPrices(stations, '2026-09-05').length, 1);
  assert.equal(selectStationPrices(stations, '2026-09-05')[0].discount.discountPrice, 2);
  assert.equal(selectStationPrices(stations, '2026-09-06').length, 0);
});

test('price comparison keeps currencies separate and treats missing IFTA as unavailable', () => {
  const cad = { currency: 'CAD', discountPrice: 2, priceAfterIfta: null };
  const usd = { currency: 'USD', discountPrice: 3, priceAfterIfta: 2.5 };
  const stats = priceStatistics([{ discount: cad }, { discount: usd }], false);
  assert.equal(stats.get('CAD').average, 2);
  assert.equal(stats.get('USD').average, 3);
  assert.equal(comparisonPrice(cad, true), null);
});

test('station price bands preserve ten-cent differences despite an outlier', () => {
  const items = [3, 3.1, 3.2, 3.3, 9].map(discountPrice =>
    ({ discount: { currency: 'USD', discountPrice } }));
  const stats = priceStatistics(items, false).get('USD');
  const palette = { low: '0,180,80', middle: '240,180,0', high: '210,30,40', unavailable: '100,100,100' };
  assert.equal(priceColor(3, stats, palette), palette.low);
  assert.notEqual(priceColor(3, stats, palette), priceColor(3.1, stats, palette));
  assert.notEqual(priceColor(3.1, stats, palette), priceColor(3.2, stats, palette));
  assert.notEqual(priceColor(3.2, stats, palette), priceColor(3.3, stats, palette));
  assert.equal(priceColor(9, stats, palette), palette.high);
});

test('widely separated expensive station prices do not collapse to the same red', () => {
  const items = [5.8, 6, 6.1, 7.7, 8].map(discountPrice =>
    ({ discount: { currency: 'USD', discountPrice } }));
  const stats = priceStatistics(items, false).get('USD');
  const palette = { low: '0,180,80', middle: '240,180,0', high: '210,30,40', unavailable: '100,100,100' };
  assert.notEqual(priceColor(6, stats, palette), priceColor(7.7, stats, palette));
  assert.equal(priceColor(5.8, stats, palette), palette.low);
  assert.equal(priceColor(8, stats, palette), palette.high);
});

test('global scale keeps a nineteen-cent price difference visually distinct', () => {
  const items = [4.25, 5.38, 5.57, 6, 7.7, 9.5].map(discountPrice =>
    ({ discount: { currency: 'CAD', discountPrice } }));
  const stats = priceStatistics(items, false).get('CAD');
  const palette = { low: '0,180,80', middle: '240,180,0', high: '210,30,40', unavailable: '100,100,100' };
  const first = priceColor(5.38, stats, palette).split(',').map(Number);
  const second = priceColor(5.57, stats, palette).split(',').map(Number);
  const colorDistance = first.reduce((sum, value, index) => sum + Math.abs(value - second[index]), 0);
  assert.ok(colorDistance >= 120, `expected strong color distance, received ${colorDistance}`);
});



test('new telemetry blends from the displayed position without snapping or rotating the long way', async () => {
  const { blendTruckPosition } = await import('../../Scripts/fleetMap/trucks/truckPlayback.js');
  const from = { latitude: 40, longitude: -80, heading: 350, speed: 40 };
  const to = { latitude: 42, longitude: -78, heading: 10, speed: 60 };
  assert.equal(blendTruckPosition(from, to, 0).latitude, 40);
  assert.equal(blendTruckPosition(from, to, .25).latitude, 40.5);
  assert.equal(blendTruckPosition(from, to, .5).latitude, 41);
  assert.equal(blendTruckPosition(from, to, .5).heading, 0);
  assert.equal(blendTruckPosition(from, to, 1), to);
});
