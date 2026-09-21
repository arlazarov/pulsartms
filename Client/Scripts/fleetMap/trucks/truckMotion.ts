import type { TruckPoint } from './truckPoints.ts';
import { mergeTruckPoints } from './truckPoints.ts';
import {
  advancePlaybackTime,
  blendTruckPosition,
  getTruckPosition,
  truckPlaybackResumeGap,
  truckTransitionDuration,
} from './truckPlayback.ts';

// One truck's motion over time: the points it has reported, the clock the
// playback reads them at, and where that leaves it this frame. The maths is
// in truckPlayback; what is kept between frames is here.
export type TruckMotion = {
  points: TruckPoint[];
  position: TruckPoint | null;
  transition: { from: TruckPoint | null; start: number } | null;
  playbackTime: number | undefined;
  playbackFrameAt: number | undefined;
};

export function createTruckMotion(): TruckMotion {
  return {
    points: [],
    position: null,
    transition: null,
    playbackTime: undefined,
    playbackFrameAt: undefined,
  };
}

// A hidden tab stops being told anything; when it comes back the truck
// starts again from the live position rather than animating the gap.
export function restartMotion(motion: TruckMotion) {
  motion.playbackTime = undefined;
  motion.playbackFrameAt = undefined;
  motion.transition = null;
}

// New telemetry. A point that moves the truck under the playback clock it
// already had is a jump, and a jump is animated rather than shown.
export function receivePoints(
  motion: TruckMotion,
  incoming: TruckPoint[],
  current: TruckPoint | null,
  time: number,
) {
  const playbackTime = motion.playbackTime ?? time;
  const before = getTruckPosition(motion.points, playbackTime);
  motion.points = mergeTruckPoints(motion.points, incoming, current, time);
  const after = getTruckPosition(motion.points, playbackTime);
  if (
    motion.position &&
    before &&
    after &&
    (Math.abs(before.latitude - after.latitude) > 1e-9 ||
      Math.abs(before.longitude - after.longitude) > 1e-9)
  ) {
    motion.transition = { from: motion.position, start: performance.now() };
  }
}

// One frame: where the truck is now, whether that is somewhere new, and
// whether it will still be moving after this frame.
export function advanceMotion(
  motion: TruckMotion,
  frameAt: number,
  time: number,
): { moved: boolean; moving: boolean } {
  const elapsed =
    motion.playbackFrameAt === undefined ? 0 : frameAt - motion.playbackFrameAt;
  motion.playbackFrameAt = frameAt;
  const first = motion.points[0]?.gpsTime;
  const latest = motion.points.at(-1)?.gpsTime;
  // A suspended tab or pruned history resumes at the buffered live position,
  // never as a six-second animation across the missing journey.
  if (
    motion.playbackTime === undefined ||
    elapsed > truckPlaybackResumeGap ||
    (first !== undefined && motion.playbackTime < first)
  ) {
    motion.playbackTime = undefined;
    motion.transition = null;
  }
  motion.playbackTime = advancePlaybackTime(
    motion.playbackTime,
    latest,
    time,
    elapsed,
  );
  // No clock means no points, and no points means nowhere to be.
  const target =
    motion.playbackTime === undefined
      ? null
      : getTruckPosition(motion.points, motion.playbackTime);
  const progress = motion.transition
    ? (performance.now() - motion.transition.start) / truckTransitionDuration
    : 1;
  const previous = motion.position;
  motion.position = blendTruckPosition(
    motion.transition?.from ?? null,
    target,
    progress,
  );
  if (progress >= 1) motion.transition = null;
  return {
    moved:
      previous?.latitude !== motion.position?.latitude ||
      previous?.longitude !== motion.position?.longitude ||
      previous?.heading !== motion.position?.heading ||
      previous?.speed !== motion.position?.speed,
    // The target advances with wall time; buffered points still need frames.
    moving:
      progress < 1 ||
      (motion.points.length > 1 &&
        motion.playbackTime !== undefined &&
        latest !== undefined &&
        motion.playbackTime < latest),
  };
}
