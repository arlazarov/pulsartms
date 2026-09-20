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
) {
  const stopData = [],
    distanceData = [];
  const previousById = new Map(previousStops.map(row => [row.id, row]));
  const fields = [
    'id',
    'position',
    'onSelect',
    'onHover',
    'number',
    'color',
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
    };
    stopData.push(row);
    if (distance)
      distanceData.push({
        position,
        text: distance,
        tones: distanceTones,
        job,
        transient: !!stop.transientLabel,
      });
  }
  layoutStopMarkers(stopData, zoom);
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
  };
}
