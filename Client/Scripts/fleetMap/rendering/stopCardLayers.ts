import type { DeckLayerFactory } from './deckLayer.ts';
import type { StopLabelStyle } from './stopLabelStyle.ts';
import { isDelivery } from './stopAppearance.ts';
import { sceneMetrics, labelSubLayers } from './sceneMetrics.ts';

// What a line of a card is. 'heading' takes the card's own accent - the
// colour of the job it belongs to - and the rest name a colour the style
// carries. 'text' names none, and falls back to the ordinary one.
export type StopCardTone =
  | 'text'
  | 'heading'
  | 'eta'
  | 'success'
  | 'danger'
  | 'muted';

// One card drawn beside a stop: where it stands, the lines it says, what
// each line is, and whether it is only there while the pointer is.
export type StopCard = {
  position: number[];
  text: string;
  job?: string;
  tones?: StopCardTone[];
  transient?: boolean;
};

const backgroundSubLayers = {
  ...labelSubLayers,
  characters: { ...labelSubLayers.characters, visible: false },
};
const lineHeight = 1.4;
// Deck's atlas allocates 1.2 em per row; compensate so card bounds match the colored row offsets.
const atlasRowScale = 1.2;

function accent(job: string | undefined, style: StopLabelStyle) {
  if (isDelivery(job)) return style.delivery;
  return /^pickup$/i.test((job || '').replace(/[\s_-]/g, ''))
    ? style.pickup
    : style.color;
}

export function stopCardLayers(
  TextLayer: DeckLayerFactory,
  distanceData: StopCard[],
  style: StopLabelStyle,
  fonts: { stopLabel: unknown },
) {
  const cards = distanceData.map(row => ({
    ...row,
    accent: accent(row.job, style),
    offset: [-style.width(row.text) / 2 || 0, -sceneMetrics.labelOffset],
  }));
  return [false, true].flatMap(transient => {
    const id = transient ? 'route-stop-hover-distance' : 'route-stop-distances';
    const data = cards.filter(row => !!row.transient === transient);
    const content = data.flatMap(card => {
      const lines = card.text.split('\n');
      return lines.map((text, index) => {
        const tone = card.tones?.[index] ?? 'text';
        const color =
          tone === 'heading'
            ? card.accent
            : tone === 'text'
              ? style.color
              : (style[tone] ?? style.color);
        return {
          position: card.position,
          text,
          tone,
          color,
          offset: [
            card.offset[0],
            card.offset[1] -
              (lines.length - 1 - index) * style.size * lineHeight,
          ],
        };
      });
    });
    const common = {
      characterSet: 'auto',
      getPosition: (row: { position: number[] }) => row.position,
      getText: (row: { text: string }) => row.text,
      getTextAnchor: 'start',
      getAlignmentBaseline: 'bottom',
      lineHeight: lineHeight / atlasRowScale,
      getPixelOffset: (row: { offset: number[] }) => row.offset,
      getSize: style.size,
      sizeUnits: 'pixels',
      fontFamily: style.fontFamily,
      fontSettings: fonts.stopLabel,
      fontWeight: 400,
      billboard: true,
      parameters: { depthCompare: 'always' },
    };
    return [
      // Full text determines one opaque card's bounds; separate rows carry semantic colors.
      new TextLayer({
        ...common,
        id,
        data,
        getColor: style.color,
        background: true,
        getBackgroundColor: style.background,
        backgroundPadding: style.padding,
        backgroundBorderRadius: style.radius,
        getBorderColor: (card: { accent: number[] }) => [...card.accent, 110],
        getBorderWidth: 1,
        _subLayerProps: backgroundSubLayers,
      }),
      new TextLayer({
        ...common,
        id: `${id}-content`,
        data: content,
        getColor: (row: { color: number[] }) => row.color,
        _subLayerProps: labelSubLayers,
      }),
    ];
  });
}
