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
  cachedVisible?: boolean;
};

// Empty miles are orange wherever they appear, dashed: nothing is on
// board. Grey, they vanished into the basemap's own roads.
const emptyColor = [234, 88, 12, 255];
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
  // A load's road is solid in its colour, an upcoming one as much as the
  // one being driven: dashed and grey, upcoming roads read as the basemap's
  // own, and several loads on one corridor were told apart by nothing.
  // Unpicked later loads are fine dashes; the picked one is solid and
  // glows (routeGlowLayers).
  const futureDashed =
    line.routeRole === 'future' && line.routeSelected !== true;
  const dashed = empty || futureDashed;
  const upcoming = line.routeRole === 'future' || line.routeRole === 'deadhead';
  const muted =
    selectionMuted ||
    line.routeRole === 'traveled' ||
    line.routeRole === 'traveled-empty' ||
    (upcoming && line.routeMuted === true);
  const extensions = dashed ? routeDashExtensions : undefined;
  const color =
    line.routeRole === 'current'
      ? colors.current
      : empty
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
          : upcoming && !line.routeSelected
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
  const traveled =
    line.routeRole === 'traveled' || line.routeRole === 'traveled-empty';
  const width = traveled
    ? metrics.routeTraveledWidth
    : upcoming && !line.routeSelected
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
  const pattern = futureDashed
    ? metrics.routeFutureDashArray
    : metrics.routeDashArray;
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

// The chosen trip's road glows: the road being driven while no later load
// is picked, or the picked later load's road - by what is selected, not by
// the load's phase. Two soft halos of the road's own colour, stronger on the
// light theme's pale ground; their strength breathes slowly with the map's
// animation (pulse, 0..1) and stands at full when it is off. Never picked.
export function routeGlowLayers(
  line: SceneRouteLine,
  PathLayer: DeckLayerFactory,
  selectionMuted = false,
  laterPicked = false,
  pulse = 1,
) {
  const chosen =
    (line.routeRole === 'current' && !laterPicked) ||
    (line.routeRole === 'future' && line.routeSelected === true);
  if (line.visible === false || selectionMuted || !chosen) return [];
  const color =
    line.routeRole === 'current'
      ? colors.current
      : line.routeColor || colors[line.routeRole] || colors.current;
  const light = globalThis.document?.documentElement?.dataset?.theme !== 'dark';
  const width = Math.max(metrics.routeCurrentMinWidth, line.strokeWeight);
  // Twenty steps of breathing: each is built once and reused.
  const step = Math.round(Math.min(1, Math.max(0, pulse)) * 20) / 20;
  const key = [color.join(','), light, width, step].join('|');
  const cached = line as SceneRouteLine & {
    cachedGlow?: unknown[];
    cachedGlowKey?: string;
    cachedGlowData?: unknown;
  };
  if (
    cached.cachedGlow &&
    cached.cachedGlowKey === key &&
    cached.cachedGlowData === line.data
  )
    return cached.cachedGlow;
  const strength = 0.72 + 0.28 * step;
  const halo = (id: string, extra: number, alpha: number) =>
    new PathLayer({
      id: `${line.id}-${id}`,
      data: line.data,
      getPath: (path: unknown) => path,
      widthUnits: 'pixels',
      capRounded: true,
      jointRounded: true,
      parameters: { depthCompare: 'always' },
      pickable: false,
      getColor: [color[0], color[1], color[2], Math.round(alpha * strength)],
      getWidth: width + extra,
      updateTriggers: { getColor: step },
    });
  const tier = light
    ? { wide: 24, near: 11, wideAlpha: 52, nearAlpha: 104 }
    : { wide: 20, near: 9, wideAlpha: 38, nearAlpha: 80 };
  cached.cachedGlowKey = key;
  cached.cachedGlowData = line.data;
  return (cached.cachedGlow = [
    halo('glow-wide', tier.wide, tier.wideAlpha),
    halo('glow', tier.near, tier.nearAlpha),
  ]);
}
