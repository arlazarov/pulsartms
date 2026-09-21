import type { DeckLayer, DeckLayerFactory } from './deckLayer.ts';
import type { SceneRouteLine } from './routeAppearance.ts';
import type { StationMark } from './stationLayers.ts';
import type { StopRow } from './stopMarkerLayout.ts';
import type { StopCard } from './stopCardLayers.ts';
import type { StopLabelStyle } from './stopLabelStyle.ts';
import type { LabelledCluster, LabelledTruck } from './truckClusters.ts';
import { memoizeLast } from './layerCache.ts';
import { routeLayers } from './routeAppearance.ts';
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
  const stations = createStationLayers({ ScatterplotLayer, TextLayer });
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
    const drawn = roads
      .sort((a, b) => ((a as any).zIndex ?? 0) - ((b as any).zIndex ?? 0))
      .flatMap(line =>
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
        ...drawn,
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
        fleet.clusters,
        ...fleet.icons,
        ...stops({ stopData, setHover, selectStop, fonts }),
        ...fleet.labels,
        ...cards,
      ] as DeckLayer[]
    ).filter(
      layer => layer.props.visible !== false && layer.props.data?.length > 0,
    );
  };
}
