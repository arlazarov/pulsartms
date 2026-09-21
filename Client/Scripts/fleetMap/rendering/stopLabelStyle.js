import { sceneMetrics } from './sceneMetrics.js';

// Fallbacks mirror the semantic light-theme roles; mounted scenes read their
// CSS tokens off the probe element. The two are held together by
// tests/fleetMap/mapColors.test.js.
/**
 * @typedef {{ color: number[], background: number[], border: number[],
 *   pickup: number[], delivery: number[], eta: number[], success: number[],
 *   danger: number[], muted: number[], size: number, padding: number[],
 *   radius: number, fontFamily: string, width: (text: string) => number }}
 *   StopLabelStyle
 */

/** @type {Readonly<StopLabelStyle>} */
export const defaultStopLabelStyle = Object.freeze({
  color: [23, 36, 56],
  background: [255, 255, 255, 255],
  border: [226, 232, 240],
  pickup: [128, 96, 50],
  delivery: [32, 122, 99],
  eta: [35, 65, 176],
  success: [20, 125, 59],
  danger: [185, 28, 28],
  muted: [71, 85, 105],
  size: sceneMetrics.stopLabelSize,
  padding: [12, 8],
  radius: 8,
  fontFamily: 'Arial, sans-serif',
  width: () => 0,
});

export function readStopLabelStyle(host) {
  const document = host.ownerDocument;
  const viewport = document?.defaultView;
  if (!document?.createElement || !viewport?.getComputedStyle || !host.append)
    return defaultStopLabelStyle;
  const probe = document.createElement('span');
  probe.className = 'fleet-map-stop-label-style';
  host.append(probe);
  const computed = viewport.getComputedStyle(probe);
  const color = value =>
    value
      .match(/[\d.]+/g)
      ?.slice(0, 3)
      .map(Number);
  const size = parseFloat(computed.fontSize) || defaultStopLabelStyle.size;
  const fontFamily = computed.fontFamily || defaultStopLabelStyle.fontFamily;
  // Start from the fallbacks so every role has a value, then replace what
  // the page actually says. Built the other way round, the six operational
  // roles only existed once the loop below had run.
  const style = {
    ...defaultStopLabelStyle,
    color: color(computed.color) || defaultStopLabelStyle.color,
    background: [
      ...(color(computed.backgroundColor) ||
        defaultStopLabelStyle.background.slice(0, 3)),
      255,
    ],
    border: color(computed.borderTopColor) || defaultStopLabelStyle.border,
    size,
    fontFamily,
    padding: [
      parseFloat(computed.paddingLeft) || 12,
      parseFloat(computed.paddingTop) || 8,
    ],
    radius: parseFloat(computed.borderTopLeftRadius) || 8,
  };
  for (const role of [
    'pickup',
    'delivery',
    'eta',
    'success',
    'danger',
    'muted',
  ]) {
    probe.className = `fleet-map-stop-label-style fleet-map-stop-label-style--${role}`;
    style[role] =
      color(viewport.getComputedStyle(probe).color) ||
      defaultStopLabelStyle[role];
  }
  probe.remove();
  const context = document.createElement('canvas').getContext('2d');
  if (context) context.font = `400 ${size}px ${fontFamily}`;
  return {
    ...style,
    width: text =>
      context
        ? Math.max(
            ...text.split('\n').map(line => context.measureText(line).width),
          )
        : 0,
  };
}
