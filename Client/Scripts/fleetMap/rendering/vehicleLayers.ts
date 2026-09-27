import type { DeckLayer, DeckLayerFactory } from './deckLayer.ts';
import type { LabelledCluster, LabelledTruck } from './truckClusters.ts';
import type { LabelFonts } from './sceneMetrics.ts';
import { truckHitIcon, truckIcon, truckState } from './truckAppearance.ts';
import { markerAnchor } from './markerAnchor.ts';
import { clusterText } from './truckLabelLayout.ts';
import { memoizeLast } from './layerCache.ts';
import { sceneMetrics as metrics, labelSubLayers } from './sceneMetrics.ts';

// A truck on the scene: where it stands, which way and how fast it is
// going, and where the layout put its number.
type Truck = LabelledTruck & {
  engine?: string;
  speed?: number;
  heading?: number;
  merged?: boolean;
};

export type VehicleLayers = {
  // The cluster badges, drawn under the stops.
  clusters: DeckLayer;
  // The trucks themselves, drawn under the stops.
  icons: DeckLayer[];
  // Their numbers, drawn over the stops.
  labels: DeckLayer[];
};

/**
 * The trucks and the badges that stand for several of them at once.
 *
 * They are drawn in two places: the truck under the stops, its number over
 * them. A badge is never moved to clear a truck - the gap between them is
 * how far the stop is - so where the two overlap the badge must be the one
 * that stays readable, and the number, which is free to move, steps around
 * it.
 */
export function createVehicleLayers({
  IconLayer,
  TextLayer,
}: {
  IconLayer: DeckLayerFactory;
  TextLayer: DeckLayerFactory;
}) {
  const parts = memoizeLast<DeckLayer[]>();
  const clusterLabels = memoizeLast<DeckLayer>();
  // The pick area goes with the marks, under the stops: over them it would
  // take a click meant for a stop's badge.
  const isIcon = (layer: DeckLayer) =>
    /^truck-(icons|hits)/.test(layer.props?.id ?? '');

  return ({
    vehicles,
    clusters,
    hoveredTruck,
    hasSelectedTruck,
    selectCluster,
    setHover,
    hoverTruck,
    selectTruck,
    fonts,
  }: {
    vehicles: Truck[];
    clusters: LabelledCluster[];
    hoveredTruck: string | null;
    hasSelectedTruck: boolean;
    selectCluster: unknown;
    setHover: unknown;
    hoverTruck: unknown;
    selectTruck: unknown;
    fonts: LabelFonts;
  }): VehicleLayers => {
    const drawn = parts(
      [vehicles, hoveredTruck, fonts, hasSelectedTruck],
      () => [
        new IconLayer({
          id: 'truck-label-anchors',
          data: vehicles.filter(
            t =>
              t.labelOffset &&
              (t.labelOffset[0] !== 0 ||
                t.labelOffset[1] !== -metrics.truckLabelOffset),
          ),
          getPosition: (t: Truck) => t.mergedPosition ?? t.position,
          getPixelOffset: (t: Truck) => t.markerOffset ?? [0, 0],
          getIcon: (t: Truck) => markerAnchor(t.labelOffset!),
          getSize: (t: Truck) => markerAnchor(t.labelOffset!).size,
          sizeUnits: 'pixels',
          billboard: true,
          pickable: true,
          onHover: hoverTruck,
          onClick: selectTruck,
          parameters: { depthCompare: 'always' },
        }),
        // The area that picks a truck is the badge's size, centred where
        // its mark is drawn, however much smaller the mark is.
        new IconLayer({
          id: 'truck-hits',
          data: vehicles.filter(t => !t.merged),
          getPosition: (t: Truck) => t.position,
          getPixelOffset: (t: Truck) => t.markerOffset ?? [0, 0],
          getIcon: () => truckHitIcon(),
          getSize: metrics.truckHitSize,
          sizeUnits: 'pixels',
          billboard: true,
          // The disc is all but transparent: nothing of it may be cut away.
          alphaCutoff: 0,
          pickable: true,
          onHover: hoverTruck,
          onClick: selectTruck,
          parameters: { depthCompare: 'always' },
        }),
        // Two icon layers, the rest first and the chosen truck last, so the
        // chosen one is drawn over them. Chosen or not, a truck keeps its
        // size.
        ...[true, false].map(
          quiet =>
            new IconLayer({
              id: quiet ? 'truck-icons-quiet' : 'truck-icons',
              // A truck standing on a stop is drawn as the ring around
              // that stop's badge, so it is not drawn again here.
              data: vehicles.filter(
                t => !t.merged && (hasSelectedTruck && !t.selected) === quiet,
              ),
              opacity: 1,
              getPosition: (t: Truck) => t.position,
              // A truck standing on a stop steps aside so the badge can
              // keep the point it marks.
              getPixelOffset: (t: Truck) => t.markerOffset ?? [0, 0],
              getIcon: (t: Truck) => truckIcon(t.engine, t.speed),
              getSize: (t: Truck) =>
                (truckState(t.engine, t.speed) === 'moving'
                  ? metrics.truckSize
                  : metrics.truckStandingSize) *
                (t.unit === hoveredTruck && !t.selected
                  ? metrics.truckHoverScale
                  : 1),
              sizeUnits: 'pixels',
              billboard: true,
              getAngle: (t: Truck) => -t.heading!,
              updateTriggers: { getSize: [hoveredTruck, hasSelectedTruck] },
              pickable: true,
              onHover: hoverTruck,
              onClick: selectTruck,
              parameters: { depthCompare: 'always' },
            }),
        ),
        new TextLayer({
          id: 'truck-numbers',
          characterSet: 'auto',
          data: vehicles,
          getPosition: (t: Truck) => t.mergedPosition ?? t.position,
          getText: (t: Truck) => t.unit,
          getSize: metrics.truckLabelSize,
          sizeUnits: 'pixels',
          getColor: [255, 255, 255, 255],
          getPixelOffset: (t: Truck) => {
            const [dx, dy] = t.labelOffset ?? [0, -metrics.truckLabelOffset];
            const [ax, ay] = t.markerOffset ?? [0, 0];
            return [dx + ax, dy + ay];
          },
          background: true,
          getBackgroundColor: (t: Truck) => [
            ...(t.selected || t.unit === hoveredTruck
              ? [49, 94, 234]
              : [30, 41, 59]),
            255,
          ],
          backgroundPadding: metrics.truckLabelPadding,
          backgroundBorderRadius: 5,
          getBorderColor: [255, 255, 255, 220],
          getBorderWidth: 1,
          updateTriggers: {
            getColor: hasSelectedTruck,
            getBorderColor: hasSelectedTruck,
            getBackgroundColor: [hoveredTruck, hasSelectedTruck],
          },
          fontFamily: 'Arial, sans-serif',
          fontSettings: fonts.truck,
          _subLayerProps: labelSubLayers,
          fontWeight: 'bold',
          billboard: true,
          pickable: true,
          onHover: hoverTruck,
          onClick: selectTruck,
          parameters: { depthCompare: 'always' },
        }),
      ],
    );
    return {
      // On the cluster's own point, and beneath the stops.
      //
      // It stays put: offsetting it to escape what it covered only trailed
      // a leader line halfway across a state as the map zoomed out. What it
      // used to cover was a stop of the route being read, so the stops are
      // drawn over it and truck labels step around it instead.
      clusters: clusterLabels(
        [clusters, selectCluster, setHover, fonts],
        () =>
          new TextLayer({
            id: 'truck-clusters',
            data: clusters,
            characterSet: 'auto',
            getPosition: (d: LabelledCluster) => d.position,
            getText: clusterText,
            getSize: metrics.truckLabelSize,
            sizeUnits: 'pixels',
            getColor: [255, 255, 255],
            background: true,
            getBackgroundColor: [30, 41, 59],
            backgroundPadding: metrics.truckClusterPadding,
            backgroundBorderRadius: metrics.truckClusterBadge,
            getBorderColor: [255, 255, 255],
            getBorderWidth: 2,
            fontFamily: 'Arial, sans-serif',
            fontSettings: fonts.truck,
            _subLayerProps: labelSubLayers,
            fontWeight: 'bold',
            billboard: true,
            pickable: true,
            onHover: setHover,
            onClick: selectCluster,
            parameters: { depthCompare: 'always' },
          }),
      ),
      icons: drawn.filter(isIcon),
      labels: drawn.filter(layer => !isIcon(layer)),
    };
  };
}
