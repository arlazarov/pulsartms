import type { TruckPoint } from './truckPoints.ts';
import { headingDifference, normalizeHeading } from './truckPoints.ts';

// Server telemetry is published roughly once a minute. Keep enough headroom for
// the publish duration and snapshot polling jitter. Missing telemetry still holds
// the last measured point; playback never extrapolates a truck's movement.
export const truckPlaybackDelay = 90000;
export const truckTransitionDuration = 6000;
export const truckPlaybackResumeGap = 1000;

// Where the playback clock stands now: never past the newest report, never
// backwards, and it resumes rather than jumps after a gap.
export function advancePlaybackTime(
  current: number,
  latest: number,
  target: number,
  elapsed: number,
): number {
  if (!Number.isFinite(latest) || !Number.isFinite(target)) return current;
  if (!Number.isFinite(current) || elapsed > truckPlaybackResumeGap)
    return Math.min(latest, target);
  return Math.min(
    latest,
    Math.max(current, target),
    current + Math.max(0, elapsed),
  );
}

// Where a truck was at a moment, read off the points it reported.
export function getTruckPosition(
  points: TruckPoint[],
  time: number,
): TruckPoint | null {
  if (!points.length) return null;
  if (time <= points[0].gpsTime) return points[0];
  if (time >= points.at(-1)!.gpsTime) return points.at(-1)!;

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
        heading: normalizeHeading(
          from.heading + headingDifference(from.heading, to.heading) * progress,
        ),
        speed: from.speed + (to.speed - from.speed) * progress,
        gpsTime: time,
      };
    }
  }
  return points.at(-1)!;
}

// Between two reported points, for the seconds the marker takes to move.
export function blendTruckPosition(
  from: TruckPoint | null,
  to: TruckPoint | null,
  progress: number,
): TruckPoint | null {
  if (!from || !to || progress >= 1) return to;
  const amount = Math.max(0, progress);
  return {
    ...to,
    latitude: from.latitude + (to.latitude - from.latitude) * amount,
    longitude: from.longitude + (to.longitude - from.longitude) * amount,
    heading: normalizeHeading(
      from.heading + headingDifference(from.heading, to.heading) * amount,
    ),
    speed: from.speed + (to.speed - from.speed) * amount,
  };
}
