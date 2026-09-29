import type { DeckLayer, DeckLayerFactory } from './deckLayer.ts';
import type { MarkPoint } from './truckClusters.ts';
import type { LabelFonts } from './sceneMetrics.ts';
import { memoizeLast } from './layerCache.ts';
import { isLightMap, lightMarkCore, markCore } from './stopAppearance.ts';
import { sceneMetrics as metrics, labelSubLayers } from './sceneMetrics.ts';
import { currentRouteColor } from './routePalette.ts';

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
// In daylight a planned fuel stop is marked in the colour of the road it
// stands on, as it was before the redesign: the ring reads as part of the
// truck's route (the owner, September 28).
const plannedInk = () =>
  isLightMap() ? currentRouteColor.slice(0, 3) : selectedBlue;
// A daylight dot is edged in a darker shade of its own price colour: flat
// on the pale map it had no edge at all and ran into the ground under it.
const daylightRim = (color: number[]) =>
  color.slice(0, 3).map(channel => Math.round(channel * 0.55));
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
      // is filled flat with its price colour - no white backing ring (the
      // owner, September 28) - and edged in a darker shade of it, so it
      // has an outline. A chosen or planned station keeps its blue rim,
      // and a planned one a heavier one: it is where the truck fuels.
      getLineWidth: (d: StationMark) =>
        d.selected || (d.recommended && isLightMap()) ? 2.5 : 1.75,
      getFillColor: isLightMap() ? (d: StationMark) => d.color : theme().core,
      getLineColor: (d: StationMark) =>
        d.selected
          ? selectedBlue
          : d.recommended
            ? plannedInk()
            : isLightMap()
              ? daylightRim(d.color)
              : d.color,
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
      // Finer and quieter on the dark map, where it glows against the
      // ground; firmer in daylight, where a faint ring was lost on the
      // road it stands on.
      getLineColor: [...plannedInk(), isLightMap() ? 235 : 170],
      getLineWidth: isLightMap() ? 2 : 1.25,
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
        // The ring says "the truck fuels here" on both maps. Daylight had
        // only the dot's blue rim and its Fuel tag, and the owner could
        // not find the planned stops on the road (September 28, evening).
        ring(
          'fuel-recommendation-rings',
          data,
          true,
          isLightMap()
            ? metrics.recommendationRadius + 2
            : metrics.recommendationRadius,
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
            // The label's ink is the theme's; in daylight that dark ink
            // sat unread on the saturated blue, so the plate there is the
            // theme's own, as on the plan's fuel badges.
            isLightMap() ? theme().core : selectedBlue,
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
