import { headingDifference, normalizeHeading } from './truckPoints.js';

// Server telemetry is published roughly once a minute. Keep enough headroom for
// the publish duration and the client's snapshot polling phase so playback never
// reaches the end of an otherwise continuous high-frequency GPS stream.
export const truckPlaybackDelay = 75000;
export const truckTransitionDuration = 6000;

export function advancePlaybackTime(current, latest, target, elapsed) {
  if (!Number.isFinite(latest) || !Number.isFinite(target)) return current;
  if (!Number.isFinite(current)) return Math.min(latest, target);
  const behind = target - current;
  const rate = behind > 1000 ? 1.1 : 1;
  return Math.min(latest, current + Math.max(0, Math.min(250, elapsed)) * rate);
}

export function getTruckPosition(points, time) {
  if (!points.length) return null;
  if (time <= points[0].gpsTime) return points[0];
  if (time >= points.at(-1).gpsTime) return points.at(-1);

  let low = 0;
  let high = points.length - 2;
  while (low <= high) {
    const middle = Math.floor((low + high) / 2);
    const from = points[middle];
    const to = points[middle + 1];
    if (time < from.gpsTime) {
      high = middle - 1;
    } else if (time > to.gpsTime) {
      low = middle + 1;
    } else {
      const progress = (time - from.gpsTime) / (to.gpsTime - from.gpsTime);
      return {
        latitude: from.latitude + (to.latitude - from.latitude) * progress,
        longitude: from.longitude + (to.longitude - from.longitude) * progress,
        heading: normalizeHeading(from.heading + headingDifference(from.heading, to.heading) * progress),
        speed: from.speed + (to.speed - from.speed) * progress,
        gpsTime: time,
      };
    }
  }
  return points.at(-1);
}

export function blendTruckPosition(from, to, progress) {
  if (!from || !to || progress >= 1) return to;
  const amount = Math.max(0, progress);
  return {
    ...to,
    latitude: from.latitude + (to.latitude - from.latitude) * amount,
    longitude: from.longitude + (to.longitude - from.longitude) * amount,
    heading: normalizeHeading(from.heading + headingDifference(from.heading, to.heading) * amount),
    speed: from.speed + (to.speed - from.speed) * amount,
  };
}
