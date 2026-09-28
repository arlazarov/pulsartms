import { isLightMap } from './stopAppearance.ts';
import { sceneMetrics as metrics } from './sceneMetrics.ts';

// The truck number and cluster tags as HUD plates (the owner, September
// 27): a glass plate with clipped corners, a fine accent outline and a
// short bright edge tick, drawn under the unchanged text layer. The text
// layer keeps its footprint and its picking; only the plate is new.
export type PlateKind = 'truck' | 'chosen' | 'cluster';

type Plate = {
  url: string;
  width: number;
  height: number;
  anchorX: number;
  anchorY: number;
};

const plates = new Map<string, Plate>();
const widths = new Map<string, number>();
let context: CanvasRenderingContext2D | null | undefined;

// The text's own drawn width in the label font, measured once per text.
function textWidth(text: string) {
  const known = widths.get(text);
  if (known !== undefined) return known;
  if (context === undefined)
    context =
      typeof document === 'undefined'
        ? null
        : document.createElement('canvas').getContext('2d');
  let width = text.length * metrics.truckLabelSize * 0.62;
  if (context) {
    context.font = `bold ${metrics.truckLabelSize}px Arial, sans-serif`;
    width = context.measureText(text).width;
  }
  widths.set(text, width);
  return width;
}

const palettes = {
  dark: {
    truck: { fill: 'rgba(7,17,31,0.86)', line: 'rgba(34,211,238,0.5)' },
    chosen: { fill: 'rgba(8,52,78,0.92)', line: 'rgb(34,211,238)' },
    cluster: { fill: 'rgba(7,17,31,0.9)', line: 'rgba(34,211,238,0.7)' },
    tick: 'rgb(103,232,249)',
    sheen: 'rgba(125,211,252,0.14)',
  },
};

// The ink the text is drawn in over its plate: graphite in daylight.
export function plateText(kind: PlateKind): number[] {
  if (isLightMap()) return [29, 37, 48];
  return kind === 'chosen' ? [236, 254, 255] : [220, 238, 250];
}

// One plate per text length, kind and theme, drawn at twice its size.
export function labelPlate(text: string, kind: PlateKind, padding: number[]) {
  const light = isLightMap();
  const width = Math.ceil(textWidth(text) + padding[0] * 2);
  const height = Math.ceil(metrics.truckLabelSize + padding[1] * 2);
  const key = `${light}|${kind}|${width}|${height}`;
  const cached = plates.get(key);
  if (cached) return cached;
  // Daylight (the owner, September 28): a compact clean white tag with a
  // thin neutral edge - the chosen truck's edge in the accent - and no
  // sheen, ticks, glow or bevel. Same size as the dark plate.
  if (light) {
    const s = 2;
    const edge = kind === 'chosen' ? 'rgb(22,119,154)' : 'rgb(211,217,224)';
    const svg =
      `<svg xmlns="http://www.w3.org/2000/svg" width="${width * s}" height="${height * s}" viewBox="0 0 ${width * s} ${height * s}">` +
      `<rect x="${s / 2}" y="${s / 2}" width="${(width - 1) * s}" height="${(height - 1) * s}" rx="${3 * s}" fill="rgb(255,255,255)" stroke="${edge}" stroke-width="${s}"/>` +
      `</svg>`;
    const plate = {
      url: `data:image/svg+xml,${encodeURIComponent(svg)}`,
      width: width * s,
      height: height * s,
      anchorX: (width * s) / 2,
      anchorY: (height * s) / 2,
    };
    plates.set(key, plate);
    return plate;
  }
  const theme = palettes.dark;
  const { fill, line } = theme[kind];
  const s = 2,
    w = width * s,
    h = height * s,
    c = (kind === 'cluster' ? 6 : 5) * s,
    i = 1;
  // Clipped top-left and bottom-right corners; a cluster clips all four.
  const outline =
    kind === 'cluster'
      ? `M${c} ${i}H${w - c}L${w - i} ${c}V${h - c}L${w - c} ${h - i}H${c}L${i} ${h - c}V${c}Z`
      : `M${c} ${i}H${w - i}V${h - c}L${w - c} ${h - i}H${i}V${c}Z`;
  const glow = kind === 'chosen' ? 0.55 : 0;
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">` +
    `<defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1">` +
    `<stop offset="0" stop-color="${theme.sheen}"/><stop offset="0.55" stop-color="${theme.sheen}" stop-opacity="0"/></linearGradient></defs>` +
    (glow
      ? `<path d="${outline}" fill="none" stroke="${line}" stroke-opacity="0.25" stroke-width="${4 * s}"/>`
      : '') +
    `<path d="${outline}" fill="${fill}" stroke="${line}" stroke-width="${s}"/>` +
    `<path d="${outline}" fill="url(#g)"/>` +
    // The bright edge ticks: along the clipped corners, like a sight's.
    `<path d="M${i + 1} ${c + 4 * s}V${c}L${c} ${i + 1}H${c + 6 * s}" fill="none" stroke="${theme.tick}" stroke-width="${1.5 * s}" stroke-linecap="square"/>` +
    `<path d="M${w - i - 1} ${h - c - 4 * s}V${h - c}L${w - c} ${h - i - 1}H${w - c - 6 * s}" fill="none" stroke="${theme.tick}" stroke-width="${1.5 * s}" stroke-linecap="square"/>` +
    `</svg>`;
  const plate = {
    url: `data:image/svg+xml,${encodeURIComponent(svg)}`,
    width: w,
    height: h,
    anchorX: w / 2,
    anchorY: h / 2,
  };
  plates.set(key, plate);
  return plate;
}

// The height a plate is drawn at, in pixels.
export function plateHeight(padding: number[]) {
  return Math.ceil(metrics.truckLabelSize + padding[1] * 2);
}
