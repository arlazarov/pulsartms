// A minute. The road under the truck is redrawn every frame; the mileage
// written beside it is not, because a number that changes faster than it
// can be read is a number nobody reads.
const interval = 60_000;

/**
 * How often the page and the stops are told how far along the truck is.
 *
 * This is deliberately not the same clock as the road: the line follows the
 * truck continuously, the figures step. The report also stops entirely
 * while a new forecast is being worked out - a mileage published against
 * arrival times that are about to change reads as a contradiction - and
 * starts again from the next figure once it is.
 */
export function createProgressReports(
  // Told the truck, the miles covered and the miles left, at most once
  // every interval.
  report: (truckId: string, miles: number, remaining: number) => void,
  // Told what the stops should count down from, including nothing at all.
  tell: (miles: number | null) => void,
) {
  let lastAt = -Infinity;
  let shown: number | null = null;
  let waiting = false;
  let total = 0;

  return {
    // How long the new route is. Said on its own, because a route that
    // keeps its stops keeps what has been reported about them.
    setTotal(miles: number) {
      total = miles;
    },
    // A route whose stops have gone: nothing said about them carries over.
    forget() {
      waiting = false;
      shown = null;
    },
    // The next figure is said whenever it is ready, rather than waiting out
    // the rest of the interval - a route that has just changed is one the
    // dispatcher is looking at.
    sayNext() {
      lastAt = -Infinity;
    },
    // Whether a new forecast is being worked out. Answers whether the wait
    // has just ended, which is when the held figure is said again.
    setWaiting(pending: boolean) {
      const resumed = waiting && !pending;
      waiting = pending;
      return resumed;
    },
    waiting: () => waiting,
    // The truck is this far along, or nowhere known.
    publish(truckId: string, miles: number | null, now: number) {
      if (waiting) return;
      if (!Number.isFinite(miles)) {
        if (shown !== null) tell(null);
        shown = null;
        lastAt = -Infinity;
        return;
      }
      if (now - lastAt < interval) return;
      lastAt = now;
      shown = miles;
      tell(miles);
      report(truckId, miles!, Math.max(0, total - miles!));
    },
  };
}
