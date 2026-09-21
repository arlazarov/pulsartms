import { layoutStopMarkers } from './stopMarkerLayout.js';

// A row of stop data as the layers read it. The scene hands these straight
// to the GPU, so a row is plain values and nothing else.
type Row = Record<string, unknown>;

const sameRows = (left: Row[], right: Row[], fields: string[]) =>
  left.length === right.length &&
  left.every((row, i) => fields.every(field => row[field] === right[i][field]));

// Snapshot plain stop data only when changed. Distances preserve geometry layers.
export function snapshotStops(
  stops: Iterable<Row>,
  previousStops: Row[] = [],
  previousDistances: Row[] = [],
  zoom: number,
  trucks: Row[] = [],
): { stopData: Row[]; distanceData: Row[] } {
  const stopData: Row[] = [],
    distanceData: Row[] = [];
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
    'standing',
    'stacked',
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
        position,
        text: distance,
        tones: distanceTones,
        job,
        transient: !!stop.transientLabel,
      });
  }
  layoutStopMarkers(stopData, zoom, trucks);
  for (let index = 0; index < stopData.length; index++) {
    const row = stopData[index],
      previous = previousById.get(row.id);
    if (previous && fields.every(field => previous[field] === row[field]))
      stopData[index] = previous;
  }
  stopData.sort((a, b) => Number(a.priority) - Number(b.priority));
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
