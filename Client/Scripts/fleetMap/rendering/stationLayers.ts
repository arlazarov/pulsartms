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
}: {
  ScatterplotLayer: DeckLayerFactory;
  TextLayer: DeckLayerFactory;
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
      getLineWidth: (d: StationMark) => (d.selected ? 3 : 2),
      getFillColor: (d: StationMark) => d.color,
      getLineColor: (d: StationMark) =>
        d.selected || d.recommended ? selectedBlue : [255, 255, 255],
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
      getColor: [255, 255, 255],
      background: true,
      getBackgroundColor: background,
      backgroundPadding: metrics.fuelVisitLabelPadding,
      backgroundBorderRadius: 4,
      getBorderColor: [255, 255, 255, 220],
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
      getLineColor: selectedBlue,
      getLineWidth: 3,
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
    // The stops of the fuel plan belong to the fuel layer: they are shown
    // while fuel is being looked at and not otherwise. Drawn always, they
    // put rings and labels over a map that had been asked to be about
    // something else.
    ...recommended(
      [stationData, stationsVisible, setHover, selectStation],
      () => {
        const data = stationData.filter(d => d.recommended && !d.editing);
        return [
          points(
            'fuel-recommendation-points',
            data,
            stationsVisible,
            setHover,
            selectStation,
            metrics.recommendationDotRadius,
          ),
          ring(
            'fuel-recommendation-rings',
            data,
            stationsVisible,
            metrics.recommendationRadius,
            setHover,
            selectStation,
          ),
        ];
      },
    ),
    visitLabels(
      [stationData, stationsVisible, setHover, selectStation, fonts.fuelVisit],
      () =>
        badge(
          'fuel-recommendation-numbers',
          stationData.filter(
            d =>
              d.recommended &&
              !d.editing &&
              typeof d.numbers === 'string' &&
              d.numbers.trim(),
          ),
          stationsVisible,
          d => `Fuel ${d.numbers}`,
          // The badge now carries how much is bought there, so its alphabet
          // is whatever the quantity and its unit need.
          'auto',
          [30, 41, 59],
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
