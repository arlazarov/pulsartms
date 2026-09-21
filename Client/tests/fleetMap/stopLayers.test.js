import test from 'node:test';
import assert from 'node:assert/strict';
import { createSceneLayers } from '../../Scripts/fleetMap/rendering/sceneLayers.ts';
import { snapshotStops } from '../../Scripts/fleetMap/rendering/stopData.ts';
import { currentRouteColor } from '../../Scripts/fleetMap/rendering/routePalette.ts';
import { stopMarkerIcon } from '../../Scripts/fleetMap/rendering/stopAppearance.ts';

test('one stop update preserves other GPU data; hover only reorders opaque pairs', () => {
  let allocations = 0;
  class Layer {
    constructor(props) {
      this.props = props;
      allocations++;
    }
  }
  const build = createSceneLayers({
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
  });
  const input = {
    lines: [],
    stationData: [],
    stationsVisible: false,
    distanceData: [],
    vehicles: [],
  };
  const stops = new Set(
    Array.from({ length: 100 }, (_, id) => ({
      id,
      position: [-80 + id * 0.01, 40],
      number: String(id + 1),
      job: id % 2 ? 'Delivery' : 'Pickup',
      transientLabel: id > 0,
    })),
  );
  let snapshot = snapshotStops(stops);
  const first = build({ ...input, stopData: snapshot.stopData });
  const byId = new Map(first.map(layer => [layer.props.id, layer]));
  assert.equal(first.length, 200);
  const currentPair = ['route-stop-0-points', 'route-stop-0-numbers'];
  assert.deepEqual(
    first.slice(-2).map(layer => layer.props.id),
    currentPair,
  );
  const next = [...stops][1];
  next.highlighted = true;
  snapshot = snapshotStops(stops, snapshot.stopData);
  allocations = 0;
  const hovered = build({ ...input, stopData: snapshot.stopData });
  // The picked badge is drawn again because it looks different - its edge
  // darkens to name the load being looked at - and nothing else is: no
  // neighbour is rebuilt, resized or moved, because badges that shuffle
  // when a load is picked are what made one hard to follow.
  assert.equal(allocations, 2, 'only the picked badge is drawn again');
  const picked = hovered.find(
    layer => layer.props.id === 'route-stop-1-points',
  );
  assert.deepEqual(
    decodeURIComponent(picked.props.iconAtlas).match(/stroke="([^"]+)"/)[1],
    'rgb(30,41,59)',
  );
  assert.equal(picked.props.getSize, 34, 'and it is not a size larger');
  assert.deepEqual(
    hovered.slice(-2).map(layer => layer.props.id),
    ['route-stop-1-points', 'route-stop-1-numbers'],
  );
  for (const layer of hovered)
    if (!layer.props.id.startsWith('route-stop-1-'))
      assert.equal(layer, byId.get(layer.props.id));
  const delivery = hovered.at(-2);
  // The picked badge is the same circle with a darker edge; everything else
  // about it - fill, size, the number on it - is unchanged.
  assert.equal(
    delivery.props.iconAtlas,
    stopMarkerIcon(currentRouteColor, [30, 41, 59, 255]).url,
  );
  assert.equal(delivery.props.getIcon(delivery.props.data[0]), 'circle');
  assert.equal(delivery.props.getSize, 34);
  assert.equal(delivery.props.sizeUnits, 'pixels');
  assert.equal(delivery.props.billboard, true);
  assert.equal(
    hovered.at(-1).props.getSize,
    15,
    'compact numbers remain readable',
  );
  assert.equal(hovered.at(-1).props.pickable, true);
  assert.equal(hovered.at(-1).props.getText(hovered.at(-1).props.data[0]), '2');
  assert.equal(
    hovered.at(-1).props.background,
    false,
    'text width cannot stretch the circle',
  );
  assert.deepEqual(hovered.at(-1).props.getColor, [255, 255, 255, 255]);
  assert.deepEqual(
    hovered.at(-1).props.getPixelOffset(hovered.at(-1).props.data[0]),
    [0, 0],
  );
  next.number = '101';
  snapshot = snapshotStops(stops, snapshot.stopData);
  allocations = 0;
  const renumbered = build({ ...input, stopData: snapshot.stopData });
  assert.equal(allocations, 2, 'only the changed stop pair receives new data');
  for (const layer of renumbered.slice(0, -2)) {
    if (layer.props.id.startsWith('route-stop-1-')) continue;
    assert.equal(layer, byId.get(layer.props.id));
    assert.equal(layer.props.data, byId.get(layer.props.id).props.data);
  }
  stops.delete(next);
  snapshot = snapshotStops(stops, snapshot.stopData);
  assert.equal(build({ ...input, stopData: snapshot.stopData }).length, 198);
});

test('one- and two-digit stop circles share fixed geometry and preserve individual picking', () => {
  class Layer {
    constructor(props) {
      this.props = props;
    }
  }
  const build = createSceneLayers({
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
  });
  const stopData = snapshotStops([
    {
      id: 1,
      position: [-80, 40],
      number: '1',
      job: 'Pickup',
      color: currentRouteColor,
    },
    {
      id: 2,
      position: [-80, 40],
      number: '12',
      job: 'Delivery',
      color: [32, 122, 99, 255],
    },
  ]).stopData;
  const selectStop = () => {};
  const layers = build({
    lines: [],
    stationData: [],
    stationsVisible: false,
    distanceData: [],
    vehicles: [],
    stopData,
    selectStop,
  });
  for (const stop of stopData) {
    const prefix = `route-stop-${stop.id}`;
    const circle = layers.find(
      layer => layer.props.id === `${prefix}-points`,
    ).props;
    const digits = layers.find(
      layer => layer.props.id === `${prefix}-numbers`,
    ).props;
    const anchor = layers.find(
      layer => layer.props.id === `${prefix}-anchor`,
    ).props;
    assert.equal(circle.getSize, 34);
    assert.equal(
      typeof circle.getIcon,
      'function',
      'packed atlas frames require an accessor',
    );
    assert.equal(circle.getIcon(stop), 'circle');
    assert.equal(
      circle.iconMapping.circle.width,
      circle.iconMapping.circle.height,
    );
    assert.equal(circle.iconAtlas, stopMarkerIcon(stop.color).url);
    assert.deepEqual(circle.getPixelOffset(stop), digits.getPixelOffset(stop));
    assert.equal(digits.background, false);
    for (const props of [circle, digits, anchor]) {
      assert.equal(props.pickable, true);
      assert.equal(props.onClick, selectStop);
      assert.equal(props.data[0], stop);
      assert.equal(props.getPosition(stop), stop.position);
    }
  }
});

test('a stop card status change invalidates label colors without rebuilding marker geometry', () => {
  const stop = {
    id: 'stop',
    position: [-80, 40],
    number: '1',
    job: 'Pickup',
    distance: 'Company\nETA Sep 9, 09:00 AM local',
    distanceTones: ['heading', 'eta'],
  };
  const stops = new Set([stop]);
  const initial = snapshotStops(stops);
  const unchanged = snapshotStops(
    stops,
    initial.stopData,
    initial.distanceData,
  );
  assert.equal(unchanged.stopData, initial.stopData);
  assert.equal(unchanged.distanceData, initial.distanceData);
  stop.distanceTones = ['heading', 'success'];
  const confirmed = snapshotStops(
    stops,
    initial.stopData,
    initial.distanceData,
  );
  assert.equal(
    confirmed.stopData,
    initial.stopData,
    'ETA color is independent of the numbered marker',
  );
  assert.notEqual(confirmed.distanceData, initial.distanceData);
  assert.deepEqual(confirmed.distanceData[0].tones, ['heading', 'success']);
  stop.job = 'Delivery';
  const delivery = snapshotStops(
    stops,
    confirmed.stopData,
    confirmed.distanceData,
  );
  assert.notEqual(
    delivery.distanceData,
    confirmed.distanceData,
    'job changes update the card accent even if text is unchanged',
  );
  assert.equal(delivery.distanceData[0].job, 'Delivery');
});
