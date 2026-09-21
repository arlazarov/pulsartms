import type { DeckLayer, DeckLayerFactory } from './deckLayer.ts';
import { sceneMetrics as metrics } from './sceneMetrics.ts';
import { currentRouteLineColor } from './routePalette.ts';

// What a stretch of road is: the one being driven, the one behind, a load
// still to come, or miles driven with nothing on board.
export type RouteRole =
  | 'current'
  | 'traveled'
  | 'future'
  | 'deadhead'
  | 'current-empty'
  | 'traveled-empty';

// A route line as the scene holds it: what to draw, how it should look, and
// the layers last built for it. The cached fields are what those layers were
// built from, so a line nothing has changed about is not built again.
export type SceneRouteLine = {
  id: string;
  data: unknown;
  strokeWeight: number;
  routeRole: RouteRole;
  routeColor?: readonly number[];
  routeMuted?: boolean;
  routeShared?: boolean;
  routeSelected?: boolean;
  visible?: boolean;
  onClick?: unknown;
  onHover?: unknown;
  onMapClick?: unknown;
  cachedLayer?: DeckLayer[] | null;
  cachedData?: unknown;
  cachedWidth?: number;
  cachedRole?: string;
  cachedColor?: string;
  cachedExtensions?: unknown;
  cachedClick?: unknown;
  cachedHover?: unknown;
  cachedMuted?: boolean;
  cachedSelected?: boolean;
  cachedShared?: boolean;
  cachedVisible?: boolean;
};

// Empty miles are grey wherever they appear: no load is on board, and the
// orange they used to share with a live route claimed attention they never
// deserved.
const emptyColor = [100, 116, 139, 235];
const colors: Record<string, readonly number[]> = {
  current: currentRouteLineColor,
  traveled: currentRouteLineColor,
  future: [145, 105, 201, 240],
  deadhead: emptyColor,
  'current-empty': emptyColor,
  'traveled-empty': emptyColor,
};
const emptyRoles = new Set(['deadhead', 'current-empty', 'traveled-empty']);
const outline = [255, 255, 255, 210];

// Each load further down the chain used to step back again. The only thing
// tying a badge to the road it belongs to is the colour they share, so
// fading the later loads faded the one signal that says which is which -
// and it was the later loads that were hardest to follow. Everything that
// is not today is quiet by the same amount, and no quieter for being
// further off.

export function routeLayers(
  line: SceneRouteLine,
  PathLayer: DeckLayerFactory,
  routeDashExtensions: unknown,
  selectionMuted = false,
) {
  const empty = emptyRoles.has(line.routeRole);
  const dashed = line.routeRole === 'future' || empty;
  const muted =
    selectionMuted ||
    line.routeRole === 'traveled' ||
    line.routeRole === 'traveled-empty' ||
    (dashed && line.routeMuted === true);
  const extensions = dashed ? routeDashExtensions : undefined;
  // A road several loads share is not any one of their colours. Pointing at
  // one of them lifts its own colour back out of the shared stretch.
  const sharedRoad = line.routeShared === true && !line.routeSelected;
  const color =
    line.routeRole === 'current'
      ? colors.current
      : sharedRoad
        ? emptyColor
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
    line.cachedShared === line.routeShared &&
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
  line.cachedShared = line.routeShared;
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
    getPath: (path: unknown) => path,
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
  const pattern = empty ? metrics.routeDotArray : metrics.routeDashArray;
  // Dash units use half-width. Both strokes must share physical dash boundaries.
  const outlineDash = pattern.map(
    (value: number) => (value * width) / outlineWidth,
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
      ...(dashed ? { getDashArray: pattern } : {}),
    }),
  ]);
}
