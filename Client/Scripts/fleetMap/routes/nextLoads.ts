import type { NextLoad } from '../contracts.d.ts';
import type { StopSelection } from './nextLoadDisplay.ts';
import { nextLoadDisplay, nextLoadKey } from './nextLoadDisplay.ts';

// What this layer draws with. Both come from the scene, which owns the
// vendor; the layer asks only for what it uses.
type NextLoadLine = {
  setOptions(options: Record<string, unknown>): void;
  setPath(path: google.maps.LatLngLiteral[]): void;
  setMap?(map: google.maps.Map | null): void;
  setVisible?(visible: boolean): void;
  map?: google.maps.Map | null;
};
type NextLoadMarker = {
  setNumber(number: string): void;
  highlighted: boolean;
  setMap?(map: google.maps.Map | null): void;
  setOptions?(options: Record<string, unknown>): void;
  setVisible?(visible: boolean): void;
  map?: google.maps.Map | null;
};

/**
 * The loads a truck could take next, drawn over the map.
 *
 * @param onSelection
 *   Which load is picked, which of its stops the card should open on, and -
 *   when the stop belongs to a leg already being driven - which leg.
 * @param reveal Asks the map to bring that load's road into view.
 */
export function createNextLoadsLayer(
  map: google.maps.Map,
  Polyline: new (options: Record<string, unknown>) => NextLoadLine,
  StopMarker: new (options: Record<string, unknown>) => NextLoadMarker,
  onSelection: (
    loadId: string | null,
    stopIndex: number,
    executionLegId?: string,
  ) => void = () => {},
  reveal: (geometry: google.maps.LatLngLiteral[] | null) => void = () => {},
) {
  const objects: (NextLoadLine | NextLoadMarker)[] = [];
  const markerUpdates: (() => void)[] = [];
  let previous: string | null = null;
  let disposed = false;
  let offset = 0;
  let cachedLoads: NextLoad[] | null = null;
  let visible = true;
  let selectedId: string | null = null;
  let selectedStopIndex: number | null = null;
  let hoveredId: string | null = null;
  let markerGroups: { marker: NextLoadMarker; members: StopSelection[] }[] = [];
  let renderedLines: { line: NextLoadLine; loadId: string }[] = [];
  // Where each load is on the ground: its roads and the stops at their ends.
  let loadGeometry = new Map<string, google.maps.LatLngLiteral[]>();
  let loadMembers = new Map<string, StopSelection>();
  const identity = (row: StopSelection) =>
    nextLoadKey(row.loadId ?? row.loadNumber, row.executionLegId);

  // Pointing at a load answers the same question as picking one - which
  // road these badges belong to - and answers it while the eye is already
  // there. A load picked on purpose outranks whatever the cursor is over,
  // so hover speaks only when nothing is picked.
  function applySelection() {
    const shown = selectedId ?? hoveredId;
    for (const group of markerGroups)
      group.marker.highlighted = group.members.some(
        row => identity(row) === shown,
      );
    for (const { line, loadId } of renderedLines)
      line?.setOptions({
        strokeWeight: 2,
        zIndex: loadId === shown ? 10 : 0,
        routeSelected: shown !== null && loadId === shown,
        routeMuted: shown !== null && loadId !== shown,
      });
  }
  // Leaving is reported by the thing being left, and the next thing can
  // report arriving first, so a departure only counts for what is current.
  function hover(key: string, over: boolean) {
    if (disposed) return;
    const next = over ? key : hoveredId === key ? null : hoveredId;
    if (hoveredId === next) return;
    hoveredId = next;
    if (selectedId === null) applySelection();
  }
  function clearSelection() {
    if (selectedId === null) return;
    selectedId = null;
    selectedStopIndex = null;
    applySelection();
    if (!disposed) onSelection(null, 0);
  }
  // A load picked on the map is a load the dispatcher wants to see. Its
  // badges stand at its own pickup and delivery, which can be several hundred
  // miles from the truck - so picking the road highlighted nothing but the
  // road, and the circles it was picked for were off the screen entirely.
  function select(member: StopSelection | undefined, loadId: string | null) {
    if (disposed || !visible || !member) return;
    selectedId = identity(member);
    selectedStopIndex = member.index;
    applySelection();
    reveal(loadGeometry.get(loadId ?? selectedId!) ?? null);
    if (member.executionLegId)
      onSelection(member.loadId ?? null, member.index, member.executionLegId);
    else onSelection(member.loadId ?? null, member.index);
  }
  function clearObjects() {
    for (const object of objects) {
      if (object.setMap) object.setMap(null);
      else object.map = null;
    }
    objects.length = 0;
    previous = null;
    markerUpdates.length = 0;
    markerGroups = [];
    renderedLines = [];
    loadGeometry = new Map();
    loadMembers = new Map();
    hoveredId = null;
  }
  return {
    clearSelection,
    clear() {
      clearSelection();
      clearObjects();
      cachedLoads = null;
    },
    setVisible(value: boolean) {
      if (disposed || visible === value) return;
      visible = value;
      if (!visible) clearSelection();
      for (const object of objects) {
        if (object.setMap) object.setOptions?.({ visible });
        else object.setVisible?.(visible);
      }
      if (visible && cachedLoads && previous === null) this.set(cachedLoads);
    },
    setStopOffset(value: number) {
      if (disposed || offset === value) return;
      offset = value;
      markerUpdates.forEach(update => update());
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      this.clear();
    },
    set(loads: NextLoad[]) {
      if (disposed) return;
      cachedLoads = loads;
      if (
        !loads.some(
          load =>
            nextLoadKey(load.id ?? load.loadNumber, load.executionLegId) ===
            selectedId,
        )
      )
        clearSelection();
      if (!visible) {
        clearObjects();
        return;
      }
      const signature = JSON.stringify(loads);
      if (signature === previous) return;
      clearObjects();
      previous = signature;
      const display = nextLoadDisplay(loads);
      const remember = (loadId: string, point: google.maps.LatLngLiteral) => {
        const known = loadGeometry.get(loadId);
        if (known) known.push(point);
        else loadGeometry.set(loadId, [point]);
      };
      renderedLines = display.lines.map(
        ({ points, role, loadId, routeColor, routeShared }) => {
          for (const p of points)
            remember(loadId, { lat: p.latitude, lng: p.longitude });
          const line = new Polyline({
            map,
            routeRole: role,
            routeColor,
            routeShared,
            strokeWeight: 2,
            onHover: (info: { object?: unknown } | null) =>
              hover(loadId, !!info?.object),
            onClick: () => select(loadMembers.get(loadId), loadId),
          });
          line.setPath(
            points.map(p => ({ lat: p.latitude, lng: p.longitude })),
          );
          objects.push(line);
          return { line, loadId };
        },
      );
      for (const { stop, numbers, members, color } of display.groups) {
        const loadId = identity(members[0]);
        remember(loadId, { lat: stop.latitude, lng: stop.longitude });
        if (!loadMembers.has(loadId)) loadMembers.set(loadId, members[0]);
        const marker = new StopMarker({
          map,
          job: stop.job,
          position: { lat: stop.latitude, lng: stop.longitude },
          number: [...numbers].map(number => number + offset).join('/'),
          color,
          transientLabel: true,
          onHover: (over: unknown) =>
            hover(identity(members[0]), over === true),
          onSelect: () => {
            if (disposed || !visible || previous !== signature) return;
            const current = members.findIndex(
              row =>
                identity(row) === selectedId && row.index === selectedStopIndex,
            );
            const row = members[(current + 1) % members.length];
            select(row, identity(row));
          },
        });
        markerUpdates.push(() =>
          marker.setNumber(
            [...numbers].map(number => number + offset).join('/'),
          ),
        );
        markerGroups.push({ marker, members });
        objects.push(marker);
      }
      applySelection();
    },
  };
}
