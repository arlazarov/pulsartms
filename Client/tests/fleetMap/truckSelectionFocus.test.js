import test from 'node:test';
import assert from 'node:assert/strict';
import { createSceneLayers } from '../../Scripts/fleetMap/rendering/sceneLayers.ts';
import { sceneMetrics as metrics } from '../../Scripts/fleetMap/rendering/sceneMetrics.ts';
import {
  labelPlate,
  plateText,
} from '../../Scripts/fleetMap/rendering/labelPlates.ts';

const padding = metrics.truckLabelPadding;

// The map's theme is the page's; without a page the light map stands.
function withDarkMap(check) {
  const outer = globalThis.document;
  globalThis.document = { documentElement: { dataset: { theme: 'dark' } } };
  try {
    check();
  } finally {
    globalThis.document = outer;
  }
}

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

// A truck is drawn smaller than a stop's badge, a standing dot smaller
// still, and choosing a truck changes no truck's size. The area that picks
// a truck keeps the badge's size.
test('choosing a truck changes no truck size, and trucks stay smaller than stops', () => {
  const chosen = truck('11006', true),
    other = truck('54777'),
    moving = { ...truck('11007'), speed: 50 };
  const layers = byId(draw([chosen, other, moving]));

  const loud = layers['truck-icons'],
    quiet = layers['truck-icons-quiet'];
  assert.deepEqual(loud.props.data, [chosen]);
  assert.deepEqual(quiet.props.data, [other, moving]);
  assert.equal(loud.props.opacity, 1);
  assert.equal(quiet.props.opacity, 1);
  assert.equal(loud.props.getSize(chosen), metrics.truckStandingSize);
  assert.equal(quiet.props.getSize(other), metrics.truckStandingSize);
  assert.equal(quiet.props.getSize(moving), metrics.truckSize);
  assert.ok(metrics.truckSize < metrics.stopBadgeDiameter);
  assert.ok(metrics.truckStandingSize < metrics.truckSize);

  // Hovering the chosen truck does not enlarge it either.
  const hovered = byId(
    scene()({
      lines: [],
      stationData: [],
      stationsVisible: false,
      stopData: [],
      distanceData: [],
      vehicles: [chosen, other],
      hasSelectedTruck: true,
      hoveredTruck: '11006',
    }),
  );
  assert.equal(
    hovered['truck-icons'].props.getSize(chosen),
    metrics.truckStandingSize,
  );

  // Picked by the badge's size, whatever is drawn, centred where the mark
  // is drawn: a truck moved aside by a stop is picked where it is shown.
  const hits = layers['truck-hits'].props;
  assert.equal(hits.pickable, true);
  assert.equal(hits.sizeUnits, 'pixels');
  assert.equal(hits.getSize, metrics.truckHitSize);
  assert.equal(hits.alphaCutoff, 0);
  assert.equal(metrics.truckHitSize, metrics.stopBadgeDiameter);
  assert.deepEqual(hits.data, [chosen, other, moving]);
  const aside = { ...truck('22001'), markerOffset: [12, -8] };
  const moved = byId(draw([aside]));
  assert.deepEqual(
    moved['truck-hits'].props.getPixelOffset(aside),
    moved['truck-icons'].props.getPixelOffset(aside),
  );
  assert.deepEqual(moved['truck-hits'].props.getPixelOffset(aside), [12, -8]);
  // The rest first, the chosen truck last, so it is drawn over them; the
  // pick area under both, and all of them under the stops.
  const order = Object.keys(layers);
  assert.ok(order.indexOf('truck-hits') < order.indexOf('truck-icons-quiet'));
  assert.ok(order.indexOf('truck-icons-quiet') < order.indexOf('truck-icons'));

  // Truck numbers stand on HUD plates (the owner, September 27): the
  // chosen truck's plate and ink are lit, the others' are plain.
  const numbers = layers['truck-numbers'].props,
    plates = layers['truck-number-plates'].props;
  assert.deepEqual(numbers.getColor(chosen), plateText('chosen'));
  assert.deepEqual(numbers.getColor(other), plateText('truck'));
  assert.equal(plates.getIcon(chosen), labelPlate('11006', 'chosen', padding));
  assert.equal(plates.getIcon(other), labelPlate('54777', 'truck', padding));
  assert.equal(plates.pickable, false, 'the text layer keeps the picking');

  // The dark map's glass marks are one size, standing or moving (the
  // owner, September 27), still smaller than a stop's badge, and choosing
  // or hovering a truck changes none of them.
  withDarkMap(() => {
    const dark = byId(draw([chosen, other, moving]));
    for (const row of [other, moving])
      assert.equal(
        dark['truck-icons-quiet'].props.getSize(row),
        metrics.truckDarkSize,
      );
    assert.equal(
      dark['truck-icons'].props.getSize(chosen),
      metrics.truckDarkSize,
    );
    assert.ok(metrics.truckDarkSize < metrics.stopBadgeDiameter);
  });
});

test('with nothing chosen every truck is drawn at full strength', () => {
  const layers = byId(draw([truck('11006'), truck('54777')]));
  assert.equal(layers['truck-icons-quiet'], undefined);
  assert.equal(layers['truck-icons'].props.data.length, 2);
  assert.equal(layers['truck-icons'].props.opacity, 1);
  const numbers = layers['truck-numbers'].props;
  for (const row of numbers.data)
    assert.deepEqual(numbers.getColor(row), plateText('truck'));
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

// The chosen truck is found by a sonar round it, not by growing: one ring,
// never picked, only for the chosen truck, and still for reduced motion.
test('the chosen truck wears a sonar that is never picked and never grows it', () => {
  const chosen = truck('11006', true),
    other = truck('54777');
  const layersAt = sonar =>
    byId(
      scene()({
        lines: [],
        stationData: [],
        stationsVisible: false,
        stopData: [],
        distanceData: [],
        vehicles: [chosen, other],
        hasSelectedTruck: true,
        sonar,
      }),
    );
  const early = layersAt(0.1)['truck-sonar'];
  const late = layersAt(0.9)['truck-sonar'];
  assert.deepEqual(early.props.data, [chosen]);
  assert.equal(early.props.pickable, false);
  assert.ok(late.props.getRadius > early.props.getRadius, 'it sweeps out');
  assert.ok(late.props.getLineColor[3] < early.props.getLineColor[3]);
  const still = layersAt('still')['truck-sonar'];
  assert.equal(
    still.props.getRadius,
    layersAt('still')['truck-sonar'].props.getRadius,
  );
  assert.equal(layersAt(null)['truck-sonar'], undefined, 'no choice, no sonar');
  // A second ring half a sweep behind; one still ring for reduced motion.
  const echo = layersAt(0.1)['truck-sonar-echo'];
  assert.equal(echo.props.pickable, false);
  assert.ok(echo.props.getRadius > early.props.getRadius);
  assert.equal(layersAt('still')['truck-sonar-echo'], undefined);
  assert.equal(
    layersAt(0.5)['truck-icons'].props.getSize(chosen),
    layersAt(null)['truck-icons'].props.getSize(chosen),
    'the chosen truck keeps its size',
  );
});
