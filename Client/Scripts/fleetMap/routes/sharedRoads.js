// @ts-check

// Several upcoming loads can run the same highway. Their geometry is the
// same road, so drawing one on top of the other loses whichever is beneath,
// and moving one aside would put it on a road it never takes. Neither is
// acceptable: the map is what a dispatcher navigates by.
//
// So the road says what is true of it. Where a load runs alone, it is drawn
// in its own colour, because nothing there is in doubt. Where several share
// the road, no one colour is true of it, and it is drawn in none of them -
// pointing at a load still lifts its own colour back out of the shared
// stretch.
//
// A hundred metres is close enough to call one road: two routings of the
// same highway sample it at different points, and the carriageways of a
// divided road are the same road to a truck.
const cell = point =>
  `${point.latitude.toFixed(3)},${point.longitude.toFixed(3)}`;

/** @param {{points: import('../contracts.d.ts').RoutePoint[], role: string, loadId: string | number}[]} lines */
export function markSharedRoads(lines) {
  const owners = new Map();
  for (const line of lines) {
    if (line.role !== 'future') continue;
    for (const point of line.points) {
      const key = cell(point);
      const loads = owners.get(key);
      if (loads) loads.add(line.loadId);
      else owners.set(key, new Set([line.loadId]));
    }
  }
  const result = [];
  for (const line of lines) {
    if (line.role !== 'future') {
      result.push(line);
      continue;
    }
    let run = null;
    for (const point of line.points) {
      const shared = (owners.get(cell(point))?.size ?? 1) > 1;
      if (run && run.routeShared === shared) {
        run.points.push(point);
        continue;
      }
      // The point that changes the answer belongs to both runs, or the road
      // would show a gap where one ends and the next begins.
      const previous = run?.points.at(-1);
      run = {
        ...line,
        points: previous ? [previous, point] : [point],
        routeShared: shared,
      };
      result.push(run);
    }
  }
  return result.filter(
    line => line.role !== 'future' || line.points.length > 1,
  );
}
