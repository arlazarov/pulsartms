import type { DeckLayer, DeckLayerFactory } from './deckLayer.ts';
import type { MarkPoint } from './truckClusters.ts';
import type { LabelFonts } from './sceneMetrics.ts';
import { memoizeLast } from './layerCache.ts';
import { isLightMap, lightMarkCore, markCore } from './stopAppearance.ts';
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
// Every station is a dot, never a pump icon (the owner, September 27). The plan's numbers are drawn in the ink: bright cyan on the dark
// map, the deep accent on the light one.
const darkInk = [34, 211, 238];
const lightInk = [14, 116, 144];
const theme = () =>
  isLightMap()
    ? { core: lightMarkCore, ink: lightInk }
    : { core: markCore, ink: darkInk };

export function createStationLayers({
  ScatterplotLayer,
  TextLayer,
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
      // On the dark map a fine rim of the price colour on the core; on the
      // pale light map a rim alone did not tell cheap from dear, so the dot
      // is filled flat with its price colour and edged in the same - no
      // white backing ring (the owner, September 28). A chosen or planned
      // station keeps its blue rim.
      getLineWidth: (d: StationMark) => (d.selected ? 2.5 : 1.75),
      getFillColor: isLightMap() ? (d: StationMark) => d.color : theme().core,
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
      getColor: theme().ink,
      background: true,
      getBackgroundColor: background,
      backgroundPadding: metrics.fuelVisitLabelPadding,
      backgroundBorderRadius: 4,
      getBorderColor: [...theme().ink, 140],
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
        points(
          'fuel-recommendation-points',
          data,
          true,
          setHover,
          selectStation,
          metrics.recommendationDotRadius * 1.2,
        ),
        // Daylight: the planned station's own blue rim and its Fuel tag
        // say it; no second ring around it.
        ...(isLightMap()
          ? []
          : [
              ring(
                'fuel-recommendation-rings',
                data,
                true,
                metrics.recommendationRadius,
                setHover,
                selectStation,
              ),
            ]),
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
        d => `Fuel ${String(d.numbers).trim()}`,
        // The badge now carries how much is bought there, so its alphabet
        // is whatever the quantity and its unit need.
        'auto',
        theme().core,
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
