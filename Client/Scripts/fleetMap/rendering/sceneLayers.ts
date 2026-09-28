import type { DeckLayer, DeckLayerFactory } from './deckLayer.ts';
import type { SceneRouteLine } from './routeAppearance.ts';
import type { StationMark } from './stationLayers.ts';
import type { StopRow } from './stopMarkerLayout.ts';
import type { StopCard } from './stopCardLayers.ts';
import type { StopLabelStyle } from './stopLabelStyle.ts';
import type { LabelledCluster, LabelledTruck } from './truckClusters.ts';
import { memoizeLast } from './layerCache.ts';
import { routeGlowLayers, routeLayers } from './routeAppearance.ts';
import { isLightMap } from './stopAppearance.ts';

const darkSonarInk = [34, 211, 238];
const lightSonarInk = [14, 116, 144];
import { createStationLayers } from './stationLayers.ts';
import { createStopLayers } from './stopLayers.ts';
import { createVehicleLayers } from './vehicleLayers.ts';
import type { LabelFonts } from './sceneMetrics.ts';
import { createLabelFonts } from './sceneMetrics.ts';
import { defaultStopLabelStyle } from './stopLabelStyle.ts';
import { stopCardLayers } from './stopCardLayers.ts';

const emptyClusters = Object.freeze<LabelledCluster[]>([]) as LabelledCluster[];

/**
 * Everything on the scene, in the order it is drawn: the roads first, the
 * fuel over them, then the trucks, the stops, and last the writing - truck
 * numbers and the cards beside a stop - which must never be covered.
 *
 * Each group keeps its own cache, so zoom-driven truck grouping does not
 * rebuild roads or stations.
 */
export function createSceneLayers({
  ScatterplotLayer,
  PathLayer,
  IconLayer,
  TextLayer,
  routeDashExtensions,
}: {
  ScatterplotLayer: DeckLayerFactory;
  PathLayer: DeckLayerFactory;
  IconLayer: DeckLayerFactory;
  TextLayer: DeckLayerFactory;
  routeDashExtensions?: unknown;
}) {
  const stations = createStationLayers({
    ScatterplotLayer,
    TextLayer,
    IconLayer,
  });
  const stops = createStopLayers({ ScatterplotLayer, IconLayer, TextLayer });
  const vehicles = createVehicleLayers({ IconLayer, TextLayer });
  const distanceLabels = memoizeLast<DeckLayer[]>();
  const labelFonts = memoizeLast<LabelFonts>();

  return ({
    lines,
    stationData,
    stationsVisible,
    stopData,
    distanceData,
    vehicles: trucks,
    clusters = emptyClusters,
    hasSelectedTruck = false,
    selectCluster,
    hoveredTruck,
    setHover,
    selectStop,
    hoverTruck,
    selectTruck,
    selectStation,
    pixelRatio = 1,
    stopLabelStyle = defaultStopLabelStyle,
    sonar = null,
    sonarBreath = 0,
    routePulse = 1,
    routeFlow = 0,
    routeFlowView = null,
  }: {
    lines: Iterable<SceneRouteLine & { map?: unknown; path?: unknown[] }>;
    stationData: StationMark[];
    stationsVisible: boolean;
    stopData: StopRow[];
    distanceData: StopCard[];
    vehicles: LabelledTruck[];
    clusters?: LabelledCluster[];
    hasSelectedTruck?: boolean;
    selectCluster?: unknown;
    hoveredTruck?: string | null;
    setHover?: unknown;
    selectStop?: unknown;
    hoverTruck?: unknown;
    selectTruck?: unknown;
    selectStation?: unknown;
    pixelRatio?: number;
    zoom?: number | null;
    stopLabelStyle?: StopLabelStyle;
    // The selected truck's sonar: 0..1 through one sweep, or 'still' for a
    // reader who asked for less motion. It says which truck is chosen, not
    // what its engine is doing, and it is never picked.
    sonar?: number | 'still' | null;
    // Under reduced motion, 0..1 through one slow change of brightness.
    sonarBreath?: number;
    // The chosen road's glow, 0..1, breathing slowly; 1 when still.
    routePulse?: number;
    // How far the chosen road's direction marks have slid, 0..1.
    routeFlow?: number;
    // Where the camera is, so the marks keep their screen spacing.
    routeFlowView?: FlowView | null;
  }): DeckLayer[] => {
    const fonts = labelFonts([pixelRatio, stopLabelStyle.size], () =>
      createLabelFonts(pixelRatio, stopLabelStyle.size),
    );
    const roads = [...lines].filter(
      line => line.map && (line.path?.length ?? 0) > 1,
    );
    // A road picked out of several quiets the others, so each one has to
    // know whether any of them was picked.
    const hasSelectedNextRoute = roads.some(
      line =>
        line.visible !== false &&
        line.routeSelected === true &&
        (line.routeRole === 'future' || line.routeRole === 'deadhead'),
    );
    const sorted = roads.sort(
      (a, b) => ((a as any).zIndex ?? 0) - ((b as any).zIndex ?? 0),
    );
    const glow = sorted.flatMap(line =>
      routeGlowLayers(
        line,
        PathLayer,
        hasSelectedNextRoute && line.routeSelected !== true,
        hasSelectedNextRoute,
        routePulse,
      ),
    );
    const flow = sorted.flatMap(line =>
      flowLayers(
        PathLayer,
        line,
        hasSelectedNextRoute && line.routeSelected !== true,
        hasSelectedNextRoute,
        routeFlow,
        routeFlowView,
      ),
    );
    const drawn = sorted.flatMap(line =>
      routeLayers(
        line,
        PathLayer,
        routeDashExtensions,
        hasSelectedNextRoute && line.routeSelected !== true,
      ),
    );
    const fleet = vehicles({
      vehicles: trucks,
      clusters,
      hoveredTruck: hoveredTruck ?? null,
      hasSelectedTruck,
      selectCluster,
      setHover,
      hoverTruck,
      selectTruck,
      fonts,
    });
    const cards = distanceLabels([distanceData, fonts, stopLabelStyle], () =>
      stopCardLayers(TextLayer, distanceData, stopLabelStyle, fonts),
    );
    return (
      [
        ...glow,
        ...drawn,
        ...flow,
        // All station fills cover roads; recommendations, stops and trucks
        // retain priority.
        ...stations({
          stationData,
          // Stations are drawn wherever the camera is. Holding them back
          // until it was close enough read as them having gone missing, and
          // the map is where fuel is decided before the route is.
          stationsVisible,
          setHover,
          selectStation,
          fonts,
        }),
        ...fleet.clusters,
        ...(sonar === null
          ? []
          : sonarLayers(ScatterplotLayer, trucks, sonar, sonarBreath)),
        ...fleet.icons,
        ...reticleLayers(IconLayer, stopData, stationData),
        ...stops({ stopData, setHover, selectStop, fonts }),
        ...fleet.labels,
        ...cards,
      ] as DeckLayer[]
    ).filter(
      layer => layer.props.visible !== false && layer.props.data?.length > 0,
    );
  };
}

// The selected truck's sonar, as the concept draws it: four thin rings a
// quarter of a slow sweep apart, each growing from just outside the mark and fading
// in and out gently. Drawn under the marks and badges, never picked;
// the mark itself keeps its size.
function sonarLayers(
  ScatterplotLayer: DeckLayerFactory,
  trucks: LabelledTruck[],
  sonar: number | 'still',
  breath = 0,
): DeckLayer[] {
  const chosen = trucks.filter(t => t.selected);
  // Bright cyan on the dark map; on the light one the deep accent, a little
  // firmer, so the rings read against a pale ground.
  const light = isLightMap();
  const ink = light ? lightSonarInk : darkSonarInk;
  // 0.45 to 1 and back, along a cosine: calm, never gone.
  const glow = 0.725 - 0.275 * Math.cos(breath * 2 * Math.PI);
  const ring = (id: string, phase: number | 'still') => {
    const eased = phase === 'still' ? 0.35 : 1 - (1 - phase) ** 2;
    // Fades in over the first tenth of a sweep and out to nothing at its
    // end, so a ring never pops in or snaps back.
    const fade =
      phase === 'still' ? glow : Math.min(1, phase / 0.1) * (1 - eased) ** 0.6;
    return new ScatterplotLayer({
      id,
      data: chosen,
      getPosition: (t: LabelledTruck) => t.position,
      radiusUnits: 'pixels',
      getRadius: 16 + eased * 30,
      stroked: true,
      filled: true,
      lineWidthUnits: 'pixels',
      getLineWidth: light ? 2 : 1.5,
      getLineColor: [...ink, Math.round(255 * fade)],
      getFillColor: [...ink, Math.round((light ? 34 : 26) * fade)],
      pickable: false,
      updateTriggers: {
        getRadius: phase,
        getLineColor: [phase, glow],
        getFillColor: [phase, glow],
      },
      parameters: { depthCompare: 'always' },
    });
  };
  return sonar === 'still'
    ? [ring('truck-sonar', 'still')]
    : [
        ring('truck-sonar', sonar),
        ring('truck-sonar-echo', (sonar + 0.25) % 1),
        ring('truck-sonar-echo-2', (sonar + 0.5) % 1),
        ring('truck-sonar-echo-3', (sonar + 0.75) % 1),
      ];
}

// The chosen stop or fuel station wears a selection reticle: a thin ring
// with four short ticks and a soft glow, under its badge so the letter stays
// readable. Static - it marks the choice, it does not animate - and never
// picked. One icon per theme, cached.
const reticleIcons = new Map<
  boolean,
  {
    url: string;
    width: number;
    height: number;
    anchorX: number;
    anchorY: number;
  }
>();
function reticleIcon(light: boolean) {
  const cached = reticleIcons.get(light);
  if (cached) return cached;
  const ink = (light ? lightSonarInk : darkSonarInk).join(',');
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="176" height="176" viewBox="0 0 44 44"><circle cx="22" cy="22" r="16.5" fill="none" stroke="rgb(${ink})" stroke-opacity="0.22" stroke-width="5"/><circle cx="22" cy="22" r="16.5" fill="none" stroke="rgb(${ink})" stroke-width="1.4"/><g stroke="rgb(${ink})" stroke-width="2" stroke-linecap="round"><path d="M22 2.5v5M22 36.5v5M2.5 22h5M36.5 22h5"/></g></svg>`;
  const icon = {
    url: `data:image/svg+xml,${encodeURIComponent(svg)}`,
    width: 176,
    height: 176,
    anchorX: 88,
    anchorY: 88,
  };
  reticleIcons.set(light, icon);
  return icon;
}

function reticleLayers(
  IconLayer: DeckLayerFactory,
  stopData: StopRow[],
  stationData: StationMark[],
): DeckLayer[] {
  const icon = reticleIcon(isLightMap());
  const stops = stopData.filter(stop => stop.selected);
  const stations = stationData.filter(station => station.selected);
  return [
    ...(stops.length
      ? [
          new IconLayer({
            id: 'stop-reticle',
            data: stops,
            getPosition: (stop: StopRow) => stop.position,
            getIcon: () => icon,
            getSize: 44,
            sizeUnits: 'pixels',
            getPixelOffset: (stop: StopRow) => [
              stop.markerOffsetX ?? 0,
              stop.markerOffsetY ?? 0,
            ],
            billboard: true,
            pickable: false,
            parameters: { depthCompare: 'always' },
          }),
        ]
      : []),
    ...(stations.length
      ? [
          new IconLayer({
            id: 'station-reticle',
            data: stations,
            getPosition: (station: StationMark) => station.position,
            getIcon: () => icon,
            getSize: 40,
            sizeUnits: 'pixels',
            billboard: true,
            pickable: false,
            parameters: { depthCompare: 'always' },
          }),
        ]
      : []),
  ];
}

// Which way the chosen road runs: now and then one pulse of light - a
// bright head with a fading tail and a soft glow - runs along the part of
// the road in view, from the truck's side towards where it is going, then
// the road rests (the owner, September 27: the arrows were too literal,
// the river too busy). Only the chosen road carries it; the others glow.
// The road's lengths are measured once per geometry; a frame cuts a few
// short pieces of it. Never picked.
export type FlowView = { zoom: number; bounds: number[] | null };
type FlowPath = {
  points: number[][];
  lengths: number[];
  total: number;
  scale: number;
};
const flowPaths = new WeakMap<object, FlowPath>();
// Screen pixels: the pulse's head and tail; the share of each cycle it
// runs, the rest being rest.
const pulseHead = 34;
const pulseTail = 190;
const pulseRun = 0.62;

function flowPath(data: unknown): FlowPath | null {
  if (!data || typeof data !== 'object') return null;
  const cached = flowPaths.get(data);
  if (cached) return cached;
  const points = (data as unknown[][])
    .flat()
    .filter(
      (point): point is number[] =>
        Array.isArray(point) &&
        Number.isFinite(point[0]) &&
        Number.isFinite(point[1]),
    );
  if (points.length < 2) return null;
  const lengths = [0];
  for (let i = 1; i < points.length; i++) {
    const [x0, y0] = points[i - 1];
    const [x1, y1] = points[i];
    const dx = (x1 - x0) * Math.cos((((y0 + y1) / 2) * Math.PI) / 180);
    lengths.push(lengths[i - 1] + Math.hypot(dx, y1 - y0));
  }
  const meanLat =
    points.reduce((sum, point) => sum + point[1], 0) / points.length;
  const path = {
    points,
    lengths,
    total: lengths.at(-1)!,
    scale: Math.cos((meanLat * Math.PI) / 180),
  };
  flowPaths.set(data, path);
  return path.total > 0 ? path : null;
}

// The piece of the road between two distances along it.
function flowPiece(path: FlowPath, from: number, to: number, hint: number) {
  const { points, lengths } = path;
  const at = (distance: number, index: number) => {
    const span = lengths[index] - lengths[index - 1] || 1;
    const t = (distance - lengths[index - 1]) / span;
    const [x0, y0] = points[index - 1];
    const [x1, y1] = points[index];
    return [x0 + (x1 - x0) * t, y0 + (y1 - y0) * t];
  };
  let i = Math.max(1, hint);
  while (i < lengths.length - 1 && lengths[i] < from) i++;
  const start = i;
  const piece = [at(from, i)];
  while (i < lengths.length - 1 && lengths[i] < to) piece.push(points[i++]);
  piece.push(at(Math.min(to, path.total), i));
  return { piece, start };
}

// Where the road is in view, as distances along it.
function visibleRange(path: FlowPath, view: FlowView) {
  const [west, south, east, north] = view.bounds ?? [-180, -90, 180, 90];
  let first = -1,
    last = -1;
  for (let i = 0; i < path.points.length; i++) {
    const [x, y] = path.points[i];
    if (x < west || x > east || y < south || y > north) continue;
    if (first < 0) first = i;
    last = i;
  }
  if (first < 0) return null;
  return [
    path.lengths[Math.max(0, first - 1)],
    path.lengths[Math.min(path.points.length - 1, last + 1)],
  ];
}

function flowLayers(
  PathLayer: DeckLayerFactory,
  line: SceneRouteLine,
  selectionMuted: boolean,
  laterPicked: boolean,
  phase: number,
  view: FlowView | null,
): DeckLayer[] {
  if (!view || line.visible === false || selectionMuted) return [];
  const chosen =
    (line.routeRole === 'current' && !laterPicked) ||
    (line.routeRole === 'future' && line.routeSelected === true);
  if (!chosen || phase >= pulseRun) return [];
  const path = flowPath(line.data);
  if (!path) return [];
  const range = visibleRange(path, view);
  if (!range) return [];
  const unit = (360 / (256 * 2 ** view.zoom)) * path.scale;
  const tail = pulseTail * unit;
  // A piece of road shorter than the pulse (the stub at the truck) runs
  // none of its own.
  if (range[1] - range[0] < tail) return [];
  // Eased across the view, fading in as it leaves and out as it arrives.
  const t = phase / pulseRun;
  const eased = 0.5 - 0.5 * Math.cos(Math.PI * t);
  const fade = Math.min(1, t / 0.12, (1 - t) / 0.18);
  const head = range[0] + (range[1] - range[0] + tail) * eased;
  const cut = (from: number, to: number) => {
    const a = Math.max(range[0], Math.min(from, path.total));
    const b = Math.max(range[0], Math.min(to, path.total, range[1]));
    return b > a ? flowPiece(path, a, b, 1).piece : null;
  };
  // The tail in five steps of falling light, then the head and its glow.
  const steps = [0.08, 0.16, 0.3, 0.5, 0.75];
  const pieces = steps
    .map((alpha, index) => ({
      piece: cut(
        head - tail + (index * tail) / steps.length,
        head - tail + ((index + 1) * tail) / steps.length,
      ),
      alpha,
    }))
    .filter(row => row.piece);
  const headPiece = cut(head - pulseHead * unit, head);
  const light = isLightMap();
  const core = light ? [255, 255, 255] : [207, 250, 254];
  const glow = light ? [14, 116, 144] : [34, 211, 238];
  const shared = {
    getPath: (row: { piece: number[][] }) => row.piece,
    widthUnits: 'pixels',
    capRounded: true,
    jointRounded: true,
    pickable: false,
    parameters: { depthCompare: 'always' },
  };
  const layers: DeckLayer[] = [
    new PathLayer({
      ...shared,
      id: `${line.id}-pulse-tail`,
      data: pieces,
      getColor: (row: { alpha: number }) => [
        ...core,
        Math.round(255 * row.alpha * fade),
      ],
      getWidth: 3,
    }),
  ];
  if (headPiece)
    layers.push(
      new PathLayer({
        ...shared,
        id: `${line.id}-pulse-glow`,
        data: [{ piece: headPiece }],
        getColor: [...glow, Math.round(110 * fade)],
        getWidth: 12,
      }),
      new PathLayer({
        ...shared,
        id: `${line.id}-pulse-head`,
        data: [{ piece: headPiece }],
        getColor: [...core, Math.round(255 * fade)],
        getWidth: 4,
      }),
    );
  return layers;
}
