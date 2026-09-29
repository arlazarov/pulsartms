import test from 'node:test';
import assert from 'node:assert/strict';
import { createSceneLayers } from '../../Scripts/fleetMap/rendering/sceneLayers.ts';
import { sceneMetrics as metrics } from '../../Scripts/fleetMap/rendering/sceneMetrics.ts';

// What the owner asked of the light map on September 28 (evening): a chosen
// stop wears the reticle as a chosen station does, a station has an
// outline, and a planned fuel stop is ringed so it can be found on the
// road. The dark map keeps what it had.
class Layer {
  constructor(props) {
    this.props = props;
  }
}
const scene = () =>
  createSceneLayers({
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
  });
const withTheme = (t, theme) => {
  const previous = globalThis.document;
  globalThis.document = { documentElement: { dataset: { theme } } };
  t.after(() => {
    globalThis.document = previous;
  });
};
const input = {
  lines: [],
  stationData: [],
  stationsVisible: true,
  stopData: [],
  distanceData: [],
  vehicles: [],
};
const byId = layers => new Map(layers.map(layer => [layer.props.id, layer]));
const green = [21, 128, 61];

for (const theme of ['light', 'dark'])
  test(`a chosen stop and a chosen station both wear the reticle on the ${theme} map`, t => {
    withTheme(t, theme);
    const layers = byId(
      scene()({
        ...input,
        stopData: [
          { id: 'a', position: [-79, 40], number: '1', selected: true },
          { id: 'b', position: [-78, 40], number: '2' },
        ],
        stationData: [
          { id: 's', position: [-77, 40], color: green, selected: true },
        ],
      }),
    );
    assert.deepEqual(
      layers.get('stop-reticle').props.data.map(stop => stop.id),
      ['a'],
    );
    assert.equal(layers.get('stop-reticle').props.pickable, false);
    assert.equal(layers.get('station-reticle').props.data.length, 1);
  });

test('a daylight station is edged in a darker shade of its price colour', t => {
  withTheme(t, 'light');
  const station = { id: 's', position: [-77, 40], color: green };
  const points = byId(scene()({ ...input, stationData: [station] })).get(
    'fuel-points',
  ).props;
  assert.deepEqual(points.getFillColor(station), green);
  const rim = points.getLineColor(station);
  assert.notDeepEqual(rim, green, 'an edge of its own, not the fill again');
  assert.ok(
    rim.every((channel, index) => channel < green[index]),
    'darker than the fill in every channel',
  );
  assert.equal(points.stroked, true);
  assert.equal(points.getLineWidth(station), 1.75);
});

test('the dark map keeps the price colour as the rim on its glass core', t => {
  withTheme(t, 'dark');
  const station = { id: 's', position: [-77, 40], color: green };
  const points = byId(scene()({ ...input, stationData: [station] })).get(
    'fuel-points',
  ).props;
  assert.deepEqual(points.getLineColor(station), green);
});

test('a planned fuel stop is ringed on both maps, firmer in daylight', t => {
  const planned = {
    id: 'p',
    position: [-77, 40],
    color: green,
    recommended: true,
  };
  const ring = theme => {
    globalThis.document = { documentElement: { dataset: { theme } } };
    return byId(scene()({ ...input, stationData: [planned] }));
  };
  const previous = globalThis.document;
  t.after(() => {
    globalThis.document = previous;
  });
  const light = ring('light');
  // The planned dot itself keeps the blue rim, heavier in daylight: read
  // while the light map stands, as the layer reads the theme when asked.
  const dot = light.get('fuel-recommendation-points').props;
  assert.deepEqual(dot.getLineColor(planned), [49, 94, 234]);
  assert.equal(dot.getLineWidth(planned), 2.5);
  const dark = ring('dark');
  for (const layers of [light, dark]) {
    assert.ok(layers.get('fuel-recommendation-points'));
    assert.ok(layers.get('fuel-recommendation-rings'));
  }
  const day = light.get('fuel-recommendation-rings').props,
    night = dark.get('fuel-recommendation-rings').props;
  assert.equal(day.getLineWidth, 2);
  assert.equal(night.getLineWidth, 1.25);
  assert.equal(day.getRadius, metrics.recommendationRadius + 2);
  assert.equal(night.getRadius, metrics.recommendationRadius);
  assert.ok(day.getLineColor[3] > night.getLineColor[3]);
});
