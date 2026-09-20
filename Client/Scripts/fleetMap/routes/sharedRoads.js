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

// About a hundred metres: two routings of the same highway sample it at
// different points, and the carriageways of a divided road are the same road
// to a truck.
const cellSize = 0.001;
const key = (latitude, longitude) =>
  `${Math.round(latitude / cellSize)},${Math.round(longitude / cellSize)}`;

// A road is claimed along its whole length, not at the points that happen to
// describe it. Asking only about the points said two loads part company
// between every pair of them, because a straight run of highway is a handful
// of points and no two routings choose the same ones.
const segmentStep = cellSize / 2;
const segmentLimit = 4000;

function claim(owners, loadId, from, to) {
  const span = Math.max(
    Math.abs(to.latitude - from.latitude),
    Math.abs(to.longitude - from.longitude),
  );
  const steps = Math.min(
    segmentLimit,
    Math.max(1, Math.ceil(span / segmentStep)),
  );
  for (let step = 0; step <= steps; step++) {
    const along = step / steps;
    const cell = key(
      from.latitude + (to.latitude - from.latitude) * along,
      from.longitude + (to.longitude - from.longitude) * along,
    );
    const loads = owners.get(cell);
    if (loads) loads.add(loadId);
    else owners.set(cell, new Set([loadId]));
  }
}

// Whether the road is claimed is asked of the cell a point sits in and the
// eight around it. Two walks of one line round to neighbouring cells here
// and there, and without this the road parts company at each of them.
function claimed(owners, point) {
  const latitude = Math.round(point.latitude / cellSize);
  const longitude = Math.round(point.longitude / cellSize);
  const loads = new Set();
  for (let down = -1; down <= 1; down++)
    for (let across = -1; across <= 1; across++)
      for (const loadId of owners.get(
        `${latitude + down},${longitude + across}`,
      ) ?? [])
        loads.add(loadId);
  return loads.size > 1;
}

// A road does not change hands for a few paces. Short stretches are the
// seams between two samplings of one highway, not a parting, and cutting the
// line at every one of them costs a drawn object apiece.
const shortestStretch = 8;

function settle(flags) {
  const runs = [];
  for (const [index, flag] of flags.entries()) {
    const last = runs.at(-1);
    if (last && last.flag === flag) last.end = index + 1;
    else runs.push({ flag, start: index, end: index + 1 });
  }
  for (const [position, run] of runs.entries()) {
    if (run.end - run.start >= shortestStretch) continue;
    const neighbour = runs[position - 1] ?? runs[position + 1];
    if (neighbour) run.flag = neighbour.flag;
  }
  const settled = [];
  for (const run of runs)
    for (let index = run.start; index < run.end; index++)
      settled.push(run.flag);
  return settled;
}

/** @param {{points: import('../contracts.d.ts').RoutePoint[], role: string, loadId: string | number}[]} lines */
export function markSharedRoads(lines) {
  const owners = new Map();
  for (const line of lines) {
    if (line.role !== 'future') continue;
    for (let index = 1; index < line.points.length; index++)
      claim(owners, line.loadId, line.points[index - 1], line.points[index]);
  }
  const result = [];
  for (const line of lines) {
    if (line.role !== 'future') {
      result.push(line);
      continue;
    }
    const flags = settle(line.points.map(point => claimed(owners, point)));
    let run = null;
    for (const [index, point] of line.points.entries()) {
      if (run && run.routeShared === flags[index]) {
        run.points.push(point);
        continue;
      }
      // The point that changes the answer belongs to both runs, or the road
      // would show a gap where one ends and the next begins.
      const previous = run?.points.at(-1);
      run = {
        ...line,
        points: previous ? [previous, point] : [point],
        routeShared: flags[index],
      };
      result.push(run);
    }
  }
  return result.filter(
    line => line.role !== 'future' || line.points.length > 1,
  );
}
