import { layoutStopMarkers } from './stopMarkerLayout.js';

const sameRows = (left, right, fields) =>
  left.length === right.length &&
  left.every((row, i) => fields.every(field => row[field] === right[i][field]));

// Snapshot plain stop data only when changed. Distances preserve geometry layers.
export function snapshotStops(
  stops,
  previousStops = [],
  previousDistances = [],
  zoom,
  trucks = [],
) {
  let stopData = [],
    distanceData = [];
  const previousById = new Map(previousStops.map(row => [row.id, row]));
  const fields = [
    'id',
    'position',
    'onSelect',
    'onHover',
    'number',
    'color',
    'done',
    'highlighted',
    'priority',
    'job',
    'markerLabel',
    'markerOffsetX',
    'markerOffsetY',
  ];
  for (const stop of stops) {
    if (stop.visible === false) continue;
    const {
      id,
      position,
      number,
      distance,
      distanceTones,
      onSelect,
      onHover,
      color,
      highlighted,
      job,
      done,
    } = stop;
    const priority = highlighted ? 2 : stop.transientLabel ? 0 : 1;
    const row = {
      id,
      position,
      number,
      onSelect,
      onHover,
      color,
      highlighted,
      priority,
      job,
      done: done === true,
    };
    stopData.push(row);
    if (distance)
      distanceData.push({
        row,
        position,
        text: distance,
        tones: distanceTones,
        job,
        transient: !!stop.transientLabel,
      });
  }
  // Stops gathered under a count are not drawn themselves, and neither is
  // the distance that would otherwise float beside a badge that is not there.
  const { clusters, clustered } = layoutStopMarkers(stopData, zoom, trucks);
  stopData = stopData.filter(row => !clustered.has(row));
  distanceData = distanceData
    .filter(entry => !clustered.has(entry.row))
    .map(({ row: _row, ...entry }) => entry);
  for (let index = 0; index < stopData.length; index++) {
    const row = stopData[index],
      previous = previousById.get(row.id);
    if (previous && fields.every(field => previous[field] === row[field]))
      stopData[index] = previous;
  }
  stopData.sort((a, b) => a.priority - b.priority);
  return {
    stopData: sameRows(stopData, previousStops, fields)
      ? previousStops
      : stopData,
    distanceData: sameRows(distanceData, previousDistances, [
      'position',
      'text',
      'tones',
      'job',
      'transient',
    ])
      ? previousDistances
      : distanceData,
    stopClusters: clusters,
  };
}
