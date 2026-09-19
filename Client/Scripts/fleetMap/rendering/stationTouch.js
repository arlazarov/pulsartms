// @ts-check
import {sceneMetrics} from './sceneMetrics.js';

/**
 * @param {{pickObject(options: {x: number, y: number, radius: number, layerIds: string[]}): unknown}} overlay
 * @param {{object?: unknown, x?: number, y?: number}} info
 * @param {any} event deck.gl pick event; pointer details vary by input device
 */
export function pickNearbyStation(overlay, info, event) {
  if (info.object || event?.tapCount > 1) return null;
  const source = event?.srcEvent?.domEvent ?? event?.srcEvent;
  if (source?.button > 0) return null;
  const pointer = source?.pointerType ?? event?.pointerType;
  const touch = pointer ? pointer === 'touch' || pointer === 'pen'
    : source?.changedTouches?.length > 0 || globalThis.matchMedia?.('(pointer: coarse)').matches;
  const {x, y} = info;
  if ((!touch && pointer !== 'mouse') || typeof x !== 'number' || typeof y !== 'number' || !Number.isFinite(x) || !Number.isFinite(y)) return null;
  // Preserve the prior mouse target and larger touch tolerance without visible halos.
  const radius = touch ? 15 : Math.max(0, sceneMetrics.stationHitRadius - sceneMetrics.stationRadius);
  return overlay.pickObject({ x, y, radius, layerIds: ['fuel-points', 'fuel-recommendation-points', 'fuel-editing-points'] });
}
