import type { DeckLayer, DeckLayerFactory } from './deckLayer.ts';
import type { MarkPoint } from './truckClusters.ts';
import type { LabelFonts } from './sceneMetrics.ts';
import { memoizeLast } from './layerCache.ts';
import { sceneMetrics as metrics, labelSubLayers } from './sceneMetrics.ts';

// A fuel station as the scene draws it: where it is, what its price makes
// it, and what the fuel plan has to say about it.
export type StationMark = {
  position: MarkPoint;
  color: number[];
  recommended?: boolean;
  selected?: boolean;
  editing?: boolean;
  numbers?: string;
};

const selectedBlue = [49, 94, 234];
// The instrument core the stop badges share, and the plan's accent.
const core = [11, 22, 38, 235];
const accent = [34, 211, 238];

// A planned fuel stop: a pump drawn in the accent on the dark core, inside
// a fine rim of the station's price colour. One icon per colour, cached.
const pumpIcons = new Map<
  string,
  {
    url: string;
    width: number;
    height: number;
    anchorX: number;
    anchorY: number;
  }
>();
function pumpIcon(color: readonly number[]) {
  const key = color.slice(0, 3).join(',');
  const cached = pumpIcons.get(key);
  if (cached) return cached;
  const rim = `rgb(${key})`;
  const ink = `rgb(${accent.join(',')})`;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10.5" fill="rgb(${core.slice(0, 3).join(',')})" fill-opacity="0.92" stroke="${rim}" stroke-width="1.6"/><g fill="none" stroke="${ink}" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M8 17V8.5A1.5 1.5 0 0 1 9.5 7h3.5A1.5 1.5 0 0 1 14.5 8.5V17M7 17h8.5M9.5 10.2h3.5"/><path d="M14.5 11l1.6 1.2v3a.9.9 0 0 0 1.8 0V10l-1.4-1.4"/></g></svg>`;
  const icon = {
    url: `data:image/svg+xml,${encodeURIComponent(svg)}`,
    width: 96,
    height: 96,
    anchorX: 48,
    anchorY: 48,
  };
  pumpIcons.set(key, icon);
  return icon;
}

/**
 * Every fuel station on the scene: the plain ones, the ones the plan
 * recommends with their rings and numbers, and the one being edited.
 *
 * Each group keeps its own cache, so a station list that has not changed is
 * never rebuilt when the camera moves.
 */
export function createStationLayers({
  ScatterplotLayer,
  TextLayer,
  IconLayer,
}: {
  ScatterplotLayer: DeckLayerFactory;
  TextLayer: DeckLayerFactory;
  IconLayer?: DeckLayerFactory;
}) {
  const plain = memoizeLast<DeckLayer>(),
    recommended = memoizeLast<DeckLayer[]>(),
    visitLabels = memoizeLast<DeckLayer>(),
    editing = memoizeLast<DeckLayer[]>();

  const points = (
    id: string,
    data: StationMark[],
    visible: boolean,
    onHover: unknown,
    onClick: unknown,
    radius: number = metrics.stationRadius,
  ) =>
    new ScatterplotLayer({
      id,
      data,
      visible,
      pickable: true,
      getPosition: (d: StationMark) => d.position,
      radiusUnits: 'pixels',
      getRadius: radius,
      stroked: true,
      lineWidthUnits: 'pixels',
      // A fine ring of the price colour on the dark core, as every mark
      // on the map is drawn; a chosen one takes the selection blue.
      getLineWidth: (d: StationMark) => (d.selected ? 2.5 : 1.75),
      getFillColor: core,
      getLineColor: (d: StationMark) =>
        d.selected || d.recommended ? selectedBlue : d.color,
      autoHighlight: true,
      highlightColor: [49, 94, 234, 100],
      onHover,
      onClick,
      parameters: { depthCompare: 'always' },
    });

  const badge = (
    id: string,
    data: StationMark[],
    visible: boolean | undefined,
    text: (d: StationMark) => string,
    characterSet: string,
    background: number[],
    offset: number,
    fonts: LabelFonts,
    onHover: unknown,
    onClick: unknown,
  ) =>
    new TextLayer({
      id,
      data,
      ...(visible === undefined ? {} : { visible }),
      characterSet,
      getPosition: (d: StationMark) => d.position,
      getText: text,
      getSize: metrics.fuelVisitLabelSize,
      sizeUnits: 'pixels',
      getPixelOffset: [0, -offset],
      getTextAnchor: 'middle',
      getAlignmentBaseline: 'center',
      getColor: accent,
      background: true,
      getBackgroundColor: background,
      backgroundPadding: metrics.fuelVisitLabelPadding,
      backgroundBorderRadius: 4,
      getBorderColor: [...accent, 140],
      getBorderWidth: 1,
      fontFamily: 'Arial, sans-serif',
      fontSettings: fonts.fuelVisit,
      _subLayerProps: labelSubLayers,
      fontWeight: 'bold',
      billboard: true,
      pickable: true,
      onHover,
      onClick,
      parameters: { depthCompare: 'always' },
    });

  const ring = (
    id: string,
    data: StationMark[],
    visible: boolean | undefined,
    radius: number,
    onHover: unknown,
    onClick: unknown,
  ) =>
    new ScatterplotLayer({
      id,
      data,
      ...(visible === undefined ? {} : { visible }),
      getPosition: (d: StationMark) => d.position,
      getRadius: radius,
      radiusUnits: 'pixels',
      filled: false,
      stroked: true,
      getLineColor: [...selectedBlue, 170],
      getLineWidth: 1.25,
      lineWidthUnits: 'pixels',
      pickable: true,
      onHover,
      onClick,
      parameters: { depthCompare: 'always' },
    });

  return ({
    stationData,
    stationsVisible,
    setHover,
    selectStation,
    fonts,
  }: {
    stationData: StationMark[];
    stationsVisible: boolean;
    setHover: unknown;
    selectStation: unknown;
    fonts: LabelFonts;
  }): DeckLayer[] => [
    plain([stationData, stationsVisible, setHover, selectStation], () =>
      points(
        'fuel-points',
        stationData.filter(d => !d.recommended),
        stationsVisible,
        setHover,
        selectStation,
      ),
    ),
    // The stops of the fuel plan are drawn whether or not the station
    // layer is on: the plan is the picked truck's, and the owner wants to
    // see where it fuels without turning every station on (September 26).
    ...recommended([stationData, setHover, selectStation], () => {
      const data = stationData.filter(d => d.recommended && !d.editing);
      return [
        IconLayer
          ? new IconLayer({
              id: 'fuel-recommendation-points',
              data,
              getPosition: (d: StationMark) => d.position,
              getIcon: (d: StationMark) => pumpIcon(d.color),
              getSize: metrics.recommendationDotRadius * 2.75,
              sizeUnits: 'pixels',
              billboard: true,
              pickable: true,
              onHover: setHover,
              onClick: selectStation,
              parameters: { depthCompare: 'always' },
            })
          : points(
              'fuel-recommendation-points',
              data,
              true,
              setHover,
              selectStation,
              metrics.recommendationDotRadius,
            ),
        ring(
          'fuel-recommendation-rings',
          data,
          true,
          metrics.recommendationRadius,
          setHover,
          selectStation,
        ),
      ];
    }),
    visitLabels([stationData, setHover, selectStation, fonts.fuelVisit], () =>
      badge(
        'fuel-recommendation-numbers',
        stationData.filter(
          d =>
            d.recommended &&
            !d.editing &&
            typeof d.numbers === 'string' &&
            d.numbers.trim(),
        ),
        true,
        d => String(d.numbers).trim(),
        // The badge now carries how much is bought there, so its alphabet
        // is whatever the quantity and its unit need.
        'auto',
        core,
        metrics.fuelVisitLabelOffset,
        fonts,
        setHover,
        selectStation,
      ),
    ),
    ...editing(
      [stationData, stationsVisible, setHover, selectStation, fonts.fuelVisit],
      () => {
        const data = stationData.filter(d => d.editing);
        return [
          points('fuel-editing-points', data, true, setHover, selectStation),
          ring(
            'fuel-editing-ring',
            data,
            undefined,
            metrics.fuelEditingRadius,
            setHover,
            selectStation,
          ),
          badge(
            'fuel-editing-label',
            data,
            // Drawn whenever there is one to draw, which is why it says
            // nothing about being visible.
            undefined,
            () => 'Editing',
            'Editing',
            selectedBlue,
            metrics.fuelEditingLabelOffset,
            fonts,
            setHover,
            selectStation,
          ),
        ];
      },
    ),
  ];
}
