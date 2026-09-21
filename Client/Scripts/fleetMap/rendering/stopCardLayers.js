import { isDelivery } from './stopAppearance.js';
import { sceneMetrics, labelSubLayers } from './sceneMetrics.ts';

const backgroundSubLayers = {
  ...labelSubLayers,
  characters: { ...labelSubLayers.characters, visible: false },
};
const lineHeight = 1.4;
// Deck's atlas allocates 1.2 em per row; compensate so card bounds match the colored row offsets.
const atlasRowScale = 1.2;

function accent(job, style) {
  if (isDelivery(job)) return style.delivery;
  return /^pickup$/i.test((job || '').replace(/[\s_-]/g, ''))
    ? style.pickup
    : style.color;
}

export function stopCardLayers(TextLayer, distanceData, style, fonts) {
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
          tone === 'heading' ? card.accent : (style[tone] ?? style.color);
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
      getPosition: row => row.position,
      getText: row => row.text,
      getTextAnchor: 'start',
      getAlignmentBaseline: 'bottom',
      lineHeight: lineHeight / atlasRowScale,
      getPixelOffset: row => row.offset,
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
        getBorderColor: card => [...card.accent, 110],
        getBorderWidth: 1,
        _subLayerProps: backgroundSubLayers,
      }),
      new TextLayer({
        ...common,
        id: `${id}-content`,
        data: content,
        getColor: row => row.color,
        _subLayerProps: labelSubLayers,
      }),
    ];
  });
}
