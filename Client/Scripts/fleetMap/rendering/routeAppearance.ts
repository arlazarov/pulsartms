import type { DeckLayer, DeckLayerFactory } from './deckLayer.ts';
import { sceneMetrics as metrics } from './sceneMetrics.ts';
import { currentRouteLineColor } from './routePalette.ts';
import { isLightMap } from './stopAppearance.ts';

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
  // In daylight a road is a crisp line of its colour in a soft haze of the
  // same colour - no white casing, a little finer (the owner, September
  // 27: the light roads were heavy). The dark map keeps its casing.
  const light = isLightMap();
  const drawnWidth = light ? Math.max(3, width * 0.75) : width;
  const outlineWidth = light
    ? drawnWidth + 5
    : width + metrics.routeOutlineWidth;
  const casing = light ? [color[0], color[1], color[2], 28] : outline;
  const dash = dashed ? { extensions, dashJustified: false } : {};
  const pattern = futureDashed
    ? metrics.routeFutureDashArray
    : metrics.routeDashArray;
  // Dash units use half-width. Both strokes must share physical dash boundaries.
  const outlineDash = pattern.map(
    (value: number) => (value * drawnWidth) / outlineWidth,
  );
  if (empty && !traveled) {
    // Empty miles as the HUD draws them (the owner, September 27: the
    // orange casing did not belong): a fine dashed amber line over a faint
    // amber halo, no white casing - still told from loaded road by colour
    // and dashes.
    const amber = isLightMap() ? [217, 119, 6] : [251, 191, 36];
    return (line.cachedLayer = [
      new PathLayer({
        ...shared,
        id: `${line.id}-outline`,
        pickable: false,
        getColor: [...amber, isLightMap() ? 50 : 40],
        getWidth: 8,
      }),
      new PathLayer({
        ...shared,
        extensions,
        dashJustified: false,
        id: line.id,
        getColor: [...amber, 235],
        getWidth: 2.5,
        getDashArray: [2.2, 2.2],
      }),
    ]);
  }
  if (traveled) {
    // The road already driven is a trace, as a HUD draws a flight path: a
    // fine line of the instrument ink over a faint wide halo of the same,
    // with no white casing; empty miles keep their dashes (the owner,
    // September 27).
    const ink = isLightMap() ? [14, 116, 144] : [34, 211, 238];
    return (line.cachedLayer = [
      new PathLayer({
        ...shared,
        id: `${line.id}-outline`,
        opacity: 1,
        pickable: false,
        getColor: [...ink, isLightMap() ? 46 : 38],
        getWidth: metrics.routeTraveledWidth * 3,
      }),
      new PathLayer({
        ...shared,
        ...dash,
        id: line.id,
        opacity: 1,
        getColor: [...ink, isLightMap() ? 190 : 170],
        getWidth: metrics.routeTraveledWidth * 0.6,
        ...(dashed ? { getDashArray: metrics.routeDashArray } : {}),
      }),
    ]);
  }
  return (line.cachedLayer = [
    new PathLayer({
      ...shared,
      ...dash,
      id: `${line.id}-outline`,
      getColor: casing,
      getWidth: outlineWidth,
      ...(dashed ? { getDashArray: outlineDash } : {}),
    }),
    new PathLayer({
      ...shared,
      ...dash,
      id: line.id,
      getColor: color,
      getWidth: drawnWidth,
      ...(dashed ? { getDashArray: pattern } : {}),
    }),
  ]);
}

// Every road ahead glows - the road being driven and each later load's -
// and the chosen one (the current road while no later load is picked, or
// the picked later load's) glows a little brighter. Two soft halos of the road's own colour, stronger on the
// light theme's pale ground; their strength breathes slowly with the map's
// animation (pulse, 0..1) and stands at full when it is off. Never picked.
export function routeGlowLayers(
  line: SceneRouteLine,
  PathLayer: DeckLayerFactory,
  selectionMuted = false,
  laterPicked = false,
  pulse = 1,
) {
  if (line.routeRole !== 'current' && line.routeRole !== 'future') return [];
  if (line.visible === false) return [];
  // Every road ahead glows, the current one and each later load's; the
  // chosen one a little brighter (the owner, September 27).
  const chosen =
    !selectionMuted &&
    ((line.routeRole === 'current' && !laterPicked) ||
      (line.routeRole === 'future' && line.routeSelected === true));
  const color =
    line.routeRole === 'current'
      ? colors.current
      : line.routeColor || colors[line.routeRole] || colors.current;
  const light = globalThis.document?.documentElement?.dataset?.theme !== 'dark';
  const width = Math.max(metrics.routeCurrentMinWidth, line.strokeWeight);
  // Twenty steps of breathing: each is built once and reused.
  const step = Math.round(Math.min(1, Math.max(0, pulse)) * 20) / 20;
  const key = [color.join(','), light, width, step, chosen].join('|');
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
  const strength = (0.72 + 0.28 * step) * (chosen ? 1 : 0.6);
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
      updateTriggers: { getColor: [step, chosen] },
    });
  // In daylight only the chosen road glows, and softly: a haze on every
  // road read as fuzz (the owner, September 27).
  if (light && !chosen) return [];
  const tier = light
    ? { wide: 10, near: 4, wideAlpha: 22, nearAlpha: 48 }
    : { wide: 20, near: 9, wideAlpha: 38, nearAlpha: 80 };
  cached.cachedGlowKey = key;
  cached.cachedGlowData = line.data;
  return (cached.cachedGlow = [
    halo('glow-wide', tier.wide, tier.wideAlpha),
    halo('glow', tier.near, tier.nearAlpha),
  ]);
}
