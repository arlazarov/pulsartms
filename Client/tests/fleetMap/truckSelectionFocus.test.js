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
test('choosing a truck quiets the rest of the fleet and nothing else', () => {
  const chosen = truck('11006', true),
    other = truck('54777');
  const layers = byId(draw([chosen, other]));

  const loud = layers['truck-icons'],
    quiet = layers['truck-icons-quiet'];
  assert.deepEqual(loud.props.data, [chosen]);
  assert.deepEqual(quiet.props.data, [other]);
  assert.equal(loud.props.opacity, 1);
  assert.equal(quiet.props.opacity, metrics.truckMutedOpacity);
  assert.equal(quiet.props.getSize(other), metrics.truckSecondarySize);
  assert.equal(loud.props.getSize(chosen), metrics.truckSize);

  // The unit numbers fade with the arrows they name.
  const numbers = layers['truck-numbers'].props;
  const faded = Math.round(255 * metrics.truckMutedOpacity);
  assert.deepEqual(numbers.getColor(other), [255, 255, 255, faded]);
  assert.deepEqual(numbers.getColor(chosen), [255, 255, 255, 255]);
  assert.deepEqual(numbers.getBackgroundColor(chosen), [49, 94, 234, 255]);
  assert.deepEqual(numbers.getBackgroundColor(other), [30, 41, 59, faded]);
  assert.deepEqual(numbers.getBorderColor(other), [
    255,
    255,
    255,
    Math.round(220 * metrics.truckMutedOpacity),
  ]);
});

test('with nothing chosen every truck is drawn at full strength', () => {
  const layers = byId(draw([truck('11006'), truck('54777')]));
  assert.equal(layers['truck-icons-quiet'], undefined);
  assert.equal(layers['truck-icons'].props.data.length, 2);
  assert.equal(layers['truck-icons'].props.opacity, 1);
  const numbers = layers['truck-numbers'].props;
  assert.deepEqual(numbers.getColor(truck('11006')), [255, 255, 255, 255]);
  assert.deepEqual(
    numbers.getBorderColor(truck('54777')),
    [255, 255, 255, 220],
  );
});

// From the design: zoomed out, only planned stops; everything else waits
// until the camera is close enough for a dot to mean a place.
test('ordinary stations wait for the camera; planned stops never do', () => {
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

  const far = draw(metrics.stationMinZoom - 1);
  assert.equal(far['fuel-points'], undefined, 'no sea of dots at a glance');
  assert.deepEqual(far['fuel-recommendation-points'].props.data, [planned]);
  assert.equal(far['fuel-recommendation-numbers'].props.visible, true);

  const near = draw(metrics.stationMinZoom);
  assert.deepEqual(near['fuel-points'].props.data, [ordinary]);
  assert.equal(near['fuel-points'].props.visible, true);

  // A scene that has not been told where the camera is draws them.
  assert.deepEqual(draw(undefined)['fuel-points'].props.data, [ordinary]);
});
