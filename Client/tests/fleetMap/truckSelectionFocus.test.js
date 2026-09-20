import test from 'node:test';
import assert from 'node:assert/strict';
import { createSceneLayers } from '../../Scripts/fleetMap/rendering/sceneLayers.js';
import { sceneMetrics as metrics } from '../../Scripts/fleetMap/rendering/sceneMetrics.js';

class Layer {
  constructor(props) {
    this.props = props;
  }
}

const truck = (unit, selected = false) => ({
  unit,
  position: [-80, 37],
  speed: 0,
  engine: 'off',
  heading: 0,
  selected,
});

const scene = () =>
  createSceneLayers({
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
  });

const draw = vehicles =>
  scene()({
    lines: [],
    stationData: [],
    stationsVisible: false,
    stopData: [],
    distanceData: [],
    vehicles,
    hasSelectedTruck: vehicles.some(row => row.selected),
  });

const byId = layers =>
  Object.fromEntries(layers.map(layer => [layer.props.id, layer]));

// A dispatcher who picked a truck is reading its route. Everything else on
// the map is context, and context that is as loud as the subject is noise.
test('choosing a truck makes the rest smaller, and leaves them solid', () => {
  const chosen = truck('11006', true),
    other = truck('54777');
  const layers = byId(draw([chosen, other]));

  const loud = layers['truck-icons'],
    quiet = layers['truck-icons-quiet'];
  assert.deepEqual(loud.props.data, [chosen]);
  assert.deepEqual(quiet.props.data, [other]);
  // Smaller, not faded: a truck half there reads as a truck whose position
  // is doubtful, and every one of them is equally real.
  assert.equal(loud.props.opacity, 1);
  assert.equal(quiet.props.opacity, 1);
  assert.equal(quiet.props.getSize(other), metrics.truckSecondarySize);
  assert.equal(loud.props.getSize(chosen), metrics.truckSize);

  const numbers = layers['truck-numbers'].props;
  assert.deepEqual(numbers.getColor, [255, 255, 255, 255]);
  assert.deepEqual(numbers.getBackgroundColor(chosen), [49, 94, 234, 255]);
  assert.deepEqual(numbers.getBackgroundColor(other), [30, 41, 59, 255]);
  assert.deepEqual(numbers.getBorderColor, [255, 255, 255, 220]);
});

test('with nothing chosen every truck is drawn at full strength', () => {
  const layers = byId(draw([truck('11006'), truck('54777')]));
  assert.equal(layers['truck-icons-quiet'], undefined);
  assert.equal(layers['truck-icons'].props.data.length, 2);
  assert.equal(layers['truck-icons'].props.opacity, 1);
  const numbers = layers['truck-numbers'].props;
  assert.deepEqual(numbers.getColor, [255, 255, 255, 255]);
  assert.deepEqual(numbers.getBorderColor, [255, 255, 255, 220]);
});

// From the design: zoomed out, only planned stops; everything else waits
// until the camera is close enough for a dot to mean a place.
// Holding stations back until the camera was close enough read as them
// having gone missing, and the map is where fuel is decided before the
// route is.
test('stations are drawn wherever the camera is', () => {
  const ordinary = { id: 'a', position: [-79, 40], color: [0, 128, 0] };
  const planned = {
    id: 'b',
    position: [-78, 40],
    color: [0, 128, 0],
    recommended: true,
    numbers: '1',
  };
  const draw = zoom =>
    byId(
      scene()({
        lines: [],
        stationData: [ordinary, planned],
        stationsVisible: true,
        stopData: [],
        distanceData: [],
        vehicles: [],
        zoom,
      }),
    );

  for (const zoom of [4, 12, undefined]) {
    const drawn = draw(zoom);
    assert.deepEqual(drawn['fuel-points'].props.data, [ordinary]);
    assert.equal(drawn['fuel-points'].props.visible, true);
    assert.deepEqual(drawn['fuel-recommendation-points'].props.data, [planned]);
    assert.equal(drawn['fuel-recommendation-numbers'].props.visible, true);
  }
});

// Two kinds of count can stand on one map. The stop count is light where the
// truck count is dark, so a glance tells them apart before the words do, and
// it goes in to where its stops part exactly as a truck count does.
test('stops under a count are one light pill that opens like a truck count', () => {
  const members = [
    { id: 'a', position: [-80.84, 35.22] },
    { id: 'b', position: [-80.7, 35.28] },
  ];
  const cluster = { id: 'a+b', count: 2, members, position: [-80.77, 35.25] };
  const open = () => true;
  const layers = byId(
    scene()({
      lines: [],
      stationData: [],
      stationsVisible: false,
      stopData: [],
      stopClusters: [cluster],
      distanceData: [],
      vehicles: [],
      selectCluster: open,
    }),
  );
  const pill = layers['stop-clusters'].props;
  assert.deepEqual(pill.data, [cluster]);
  assert.equal(pill.getText(cluster), '2 stops');
  assert.deepEqual(pill.getBackgroundColor, [255, 255, 255]);
  assert.deepEqual(pill.getColor, [30, 41, 59]);
  assert.equal(pill.pickable, true);
  assert.equal(pill.onClick, open, 'the same way in as a truck count');
});
