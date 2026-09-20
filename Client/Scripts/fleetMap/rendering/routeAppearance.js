import { sceneMetrics as metrics } from './sceneMetrics.js';
import { currentRouteLineColor } from './routePalette.js';

const colors = {
  current: currentRouteLineColor,
  traveled: currentRouteLineColor,
  future: [145, 105, 201, 240],
  deadhead: [220, 145, 48, 190],
  'current-empty': [220, 145, 48, 255],
  'traveled-empty': [220, 145, 48, 255],
};
const outline = [255, 255, 255, 210];

export function routeLayers(
  line,
  PathLayer,
  routeDashExtensions,
  selectionMuted = false,
) {
  const dashed = line.routeRole === 'future' || line.routeRole === 'deadhead';
  const muted =
    selectionMuted ||
    line.routeRole === 'traveled' ||
    line.routeRole === 'traveled-empty' ||
    (dashed && line.routeMuted === true);
  const extensions = dashed ? routeDashExtensions : undefined;
  const color =
    line.routeRole === 'current'
      ? colors.current
      : line.routeColor || colors[line.routeRole] || colors.current;
  const colorKey = color.join(',');
  if (
    line.cachedLayer &&
    line.cachedData === line.data &&
    line.cachedWidth === line.strokeWeight &&
    line.cachedRole === line.routeRole &&
    line.cachedColor === colorKey &&
    line.cachedExtensions === extensions &&
    line.cachedClick === line.onClick &&
    line.cachedHover === line.onHover &&
    line.cachedMuted === muted &&
    line.cachedSelected === line.routeSelected &&
    line.cachedVisible === (line.visible !== false)
  )
    return line.cachedLayer;
  line.cachedData = line.data;
  line.cachedWidth = line.strokeWeight;
  line.cachedRole = line.routeRole;
  line.cachedColor = colorKey;
  line.cachedExtensions = extensions;
  line.cachedClick = line.onClick;
  line.cachedHover = line.onHover;
  line.cachedMuted = muted;
  line.cachedSelected = line.routeSelected;
  line.cachedVisible = line.visible !== false;
  const shared = {
    data: line.data,
    visible: line.visible !== false,
    // Work that is not today's steps back by default: upcoming loads and
    // their empty miles were drawn as strongly as the road being driven.
    opacity:
      line.routeRole === 'traveled' || line.routeRole === 'traveled-empty'
        ? metrics.routeTraveledOpacity
        : muted
          ? metrics.routeMutedOpacity
          : dashed && !line.routeSelected
            ? metrics.routeFutureOpacity
            : 1,
    getPath: path => path,
    widthUnits: 'pixels',
    capRounded: true,
    jointRounded: true,
    parameters: { depthCompare: 'always' },
    pickable: !!(line.onMapClick || line.onClick || line.onHover),
    onClick: line.onMapClick ?? line.onClick,
    onHover: line.onHover,
  };
  const width =
    dashed && !line.routeSelected
      ? metrics.routeSecondaryWidth
      : Math.max(
          line.routeRole === 'current'
            ? metrics.routeCurrentMinWidth
            : metrics.routeMinWidth,
          line.strokeWeight *
            (line.routeRole === 'current'
              ? metrics.routeCurrentWidthScale
              : metrics.routeWidthScale),
        );
  const outlineWidth = width + metrics.routeOutlineWidth;
  const dash = dashed ? { extensions, dashJustified: false } : {};
  // Dash units use half-width. Both strokes must share physical dash boundaries.
  const outlineDash = metrics.routeDashArray.map(
    value => (value * width) / outlineWidth,
  );
  return (line.cachedLayer = [
    new PathLayer({
      ...shared,
      ...dash,
      id: `${line.id}-outline`,
      getColor: outline,
      getWidth: outlineWidth,
      ...(dashed ? { getDashArray: outlineDash } : {}),
    }),
    new PathLayer({
      ...shared,
      ...dash,
      id: line.id,
      getColor: color,
      getWidth: width,
      ...(dashed ? { getDashArray: metrics.routeDashArray } : {}),
    }),
  ]);
}
