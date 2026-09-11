import { sceneMetrics as metrics } from './sceneMetrics.js';
import { currentRouteLineColor } from './routePalette.js';

const colors = {
  current: currentRouteLineColor,
  future: [145, 105, 201, 240],
  deadhead: [220, 145, 48, 190],
};
const outline = [255, 255, 255, 210];

export function routeLayers(line, PathLayer, routeDashExtensions, selectionMuted = false) {
  const dashed = line.routeRole === 'future' || line.routeRole === 'deadhead';
  const muted = selectionMuted || dashed && line.routeMuted === true;
  const extensions = dashed ? routeDashExtensions : undefined;
  const color = line.routeRole === 'current' ? colors.current : line.routeColor || colors[line.routeRole] || colors.current;
  const colorKey = color.join(',');
  if (line.cachedLayer && line.cachedData === line.data
    && line.cachedWidth === line.strokeWeight && line.cachedRole === line.routeRole
    && line.cachedColor === colorKey
    && line.cachedExtensions === extensions
    && line.cachedMuted === muted
    && line.cachedVisible === (line.visible !== false))
    return line.cachedLayer;
  line.cachedData = line.data;
  line.cachedWidth = line.strokeWeight;
  line.cachedRole = line.routeRole;
  line.cachedColor = colorKey;
  line.cachedExtensions = extensions;
  line.cachedMuted = muted;
  line.cachedVisible = line.visible !== false;
  const shared = {
    data: line.data,
    visible: line.visible !== false,
    opacity: muted ? metrics.routeMutedOpacity : 1,
    getPath: path => path,
    widthUnits: 'pixels',
    capRounded: true,
    jointRounded: true,
    parameters: { depthCompare: 'always' },
  };
  const width = Math.max(line.routeRole === 'current' ? metrics.routeCurrentMinWidth : metrics.routeMinWidth,
    line.strokeWeight * (line.routeRole === 'current' ? metrics.routeCurrentWidthScale : metrics.routeWidthScale));
  const outlineWidth = width + metrics.routeOutlineWidth;
  const dash = dashed ? { extensions, dashJustified: false } : {};
  // Dash units use half-width. Both strokes must share physical dash boundaries.
  const outlineDash = metrics.routeDashArray.map(value => value * width / outlineWidth);
  return (line.cachedLayer = [
    new PathLayer({ ...shared, ...dash, id: `${line.id}-outline`, getColor: outline, getWidth: outlineWidth,
      ...(dashed ? {getDashArray: outlineDash} : {}) }),
    new PathLayer({ ...shared, ...dash, id: line.id, getColor: color, getWidth: width,
      ...(dashed ? {getDashArray: metrics.routeDashArray} : {}) }),
  ]);
}
