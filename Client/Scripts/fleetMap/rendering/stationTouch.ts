import { sceneMetrics } from './sceneMetrics.ts';

// A tap near a station counts as a tap on it: fingers are wider than the
// dot, and the miss would otherwise land on the map.
export function pickNearbyStation(
  overlay: {
    pickObject(options: Record<string, unknown>): { object?: unknown } | null;
  },
  info: { object?: unknown; x?: number; y?: number },
  event: Record<string, any> | undefined,
) {
  if (info.object || event?.tapCount > 1) return null;
  const source = event?.srcEvent?.domEvent ?? event?.srcEvent;
  if (source?.button > 0) return null;
  const pointer = source?.pointerType ?? event?.pointerType;
  const touch = pointer
    ? pointer === 'touch' || pointer === 'pen'
    : source?.changedTouches?.length > 0 ||
      globalThis.matchMedia?.('(pointer: coarse)').matches;
  if (
    (!touch && pointer !== 'mouse') ||
    !Number.isFinite(info.x) ||
    !Number.isFinite(info.y)
  )
    return null;
  // Preserve the prior mouse target and larger touch tolerance without visible halos.
  const radius = touch
    ? 15
    : Math.max(0, sceneMetrics.stationHitRadius - sceneMetrics.stationRadius);
  return overlay.pickObject({
    x: info.x,
    y: info.y,
    radius,
    layerIds: [
      'fuel-points',
      'fuel-recommendation-points',
      'fuel-editing-points',
    ],
  });
}
