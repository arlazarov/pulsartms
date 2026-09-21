import type { DeckLayer, DeckLayerFactory } from './deckLayer.ts';
import type { StopRow } from './stopMarkerLayout.ts';
import type { LabelFonts } from './sceneMetrics.ts';
import { memoizeLast } from './layerCache.ts';
import { stopAppearance, stopMarkerIcon } from './stopAppearance.ts';
import { sceneMetrics as metrics, labelSubLayers } from './sceneMetrics.ts';

// What a stop's three layers were built from. Rebuilt only when one of
// these changes, so a stop nothing has happened to keeps its layers - and
// with them its place in the draw order.
const fields = [
  'position',
  'number',
  'job',
  'color',
  'done',
  'onSelect',
  'onHover',
  'markerLabel',
  'markerOffsetX',
  'markerOffsetY',
  'standing',
  'stacked',
  'highlighted',
] as const;

type Cached = {
  stop: StopRow;
  setHover: unknown;
  selectStop: unknown;
  fonts: LabelFonts;
  layers: DeckLayer[];
};

/**
 * The badge at every stop: the dot on the point it marks, the circle with
 * its number, and the number itself. Each stop keeps its own three layers
 * so that reordering one does not rebuild the rest.
 */
export function createStopLayers({
  ScatterplotLayer,
  IconLayer,
  TextLayer,
}: {
  ScatterplotLayer: DeckLayerFactory;
  IconLayer: DeckLayerFactory;
  TextLayer: DeckLayerFactory;
}) {
  const perStop = new Map<string, Cached>();
  const group = memoizeLast<DeckLayer[]>();

  return ({
    stopData,
    setHover,
    selectStop,
    fonts,
  }: {
    stopData: StopRow[];
    setHover: unknown;
    selectStop: unknown;
    fonts: LabelFonts;
  }): DeckLayer[] =>
    group([stopData, setHover, selectStop, fonts], () => {
      const active = new Set(stopData.map(stop => stop.id));
      for (const id of perStop.keys()) if (!active.has(id)) perStop.delete(id);
      const drawn = stopData.flatMap(stop => {
        const id = `route-stop-${stop.id}`;
        const cached = perStop.get(stop.id);
        if (
          cached &&
          cached.fonts === fonts &&
          cached.setHover === setHover &&
          cached.selectStop === selectStop &&
          fields.every(field => cached.stop[field] === stop[field])
        )
          return cached.layers;
        const data = [stop];
        const appearance = stopAppearance(stop.job, stop.color, stop.done);
        // The load being looked at is named on its own circles: the road
        // was emphasised and the badges were not, so picking a load lit
        // everything except the stops it was picked for. It is the badge's
        // own edge that darkens - nothing is added beside it and nothing
        // grows, because a badge that grows pushes its neighbours aside,
        // and badges that shuffle when a load is picked are what made one
        // hard to follow in the first place.
        const { url: iconAtlas, ...circle } = stopMarkerIcon(
          appearance.fill,
          stop.highlighted && !stop.standing
            ? metrics.stopBadgePickedEdge
            : appearance.border,
          stop.done ? metrics.stopBadgeDoneRadius : undefined,
          stop.standing,
          stop.stacked,
        );
        const layers = [
          ...(stop.markerOffsetX || stop.markerOffsetY
            ? [
                new ScatterplotLayer({
                  id: `${id}-anchor`,
                  data,
                  getPosition: (s: StopRow) => s.position,
                  getRadius: metrics.stopRadius,
                  radiusUnits: 'pixels',
                  getFillColor: appearance.fill,
                  opacity: 1,
                  stroked: true,
                  getLineColor: appearance.border,
                  getLineWidth: 1,
                  lineWidthUnits: 'pixels',
                  pickable: true,
                  onHover: setHover,
                  onClick: selectStop,
                  parameters: { depthCompare: 'always' },
                }),
              ]
            : []),
          new IconLayer({
            id: `${id}-points`,
            data,
            getPosition: (s: StopRow) => s.position,
            iconAtlas,
            iconMapping: { circle: { ...circle, x: 0, y: 0 } },
            // Deck resolves packed frames through an accessor, not a constant attribute.
            getIcon: () => 'circle',
            getSize: stop.standing
              ? metrics.stopBadgeStandingDiameter
              : stop.stacked
                ? metrics.stopBadgeStackedDiameter
                : metrics.stopBadgeDiameter,
            sizeUnits: 'pixels',
            getPixelOffset: (s: StopRow) => [s.markerOffsetX, s.markerOffsetY],
            billboard: true,
            pickable: true,
            onHover: setHover,
            onClick: selectStop,
            parameters: { depthCompare: 'always' },
          }),
          new TextLayer({
            id: `${id}-numbers`,
            characterSet: '0123456789/',
            data,
            getPosition: (s: StopRow) => s.position,
            getText: (s: StopRow) => s.markerLabel,
            getPixelOffset: (s: StopRow) => [s.markerOffsetX, s.markerOffsetY],
            getTextAnchor: 'middle',
            getAlignmentBaseline: 'center',
            getSize: metrics.numberSize,
            sizeUnits: 'pixels',
            getColor: appearance.text,
            background: false,
            fontFamily: 'Arial, sans-serif',
            fontSettings: fonts.stop,
            _subLayerProps: labelSubLayers,
            fontWeight: 'bold',
            billboard: true,
            pickable: true,
            onHover: setHover,
            onClick: selectStop,
            parameters: { depthCompare: 'always' },
          }),
        ];
        perStop.set(stop.id, { stop, setHover, selectStop, fonts, layers });
        return layers;
      });
      // Every dot that marks where a stop really is lies under every badge.
      // Drawn stop by stop, the dot of a later stop landed on the badge of
      // an earlier one - two coloured specks across the "3".
      const dot = (layer: DeckLayer) =>
        ((layer as { id?: string }).id ?? layer.props?.id)?.endsWith('-anchor');
      return [...drawn.filter(dot), ...drawn.filter(layer => !dot(layer))];
    });
}
