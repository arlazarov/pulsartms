import { nextLoadDisplay, nextLoadKey } from './nextLoadDisplay.js';

export function createNextLoadsLayer(
  map,
  Polyline,
  StopMarker,
  onSelection = () => {},
) {
  const objects = [];
  const markerUpdates = [];
  let previous = null;
  let disposed = false;
  let offset = 0;
  let cachedLoads = null;
  let visible = true;
  let selectedId = null;
  let selectedStopIndex = null;
  let hoveredId = null;
  let markerGroups = [];
  let renderedLines = [];
  const identity = row =>
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
  function hover(key, over) {
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
    hoveredId = null;
  }
  return {
    clearSelection,
    clear() {
      clearSelection();
      clearObjects();
      cachedLoads = null;
    },
    setVisible(value) {
      if (disposed || visible === value) return;
      visible = value;
      if (!visible) clearSelection();
      for (const object of objects) {
        if (object.setMap) object.setOptions({ visible });
        else object.setVisible(visible);
      }
      if (visible && cachedLoads && previous === null) this.set(cachedLoads);
    },
    setStopOffset(value) {
      if (disposed || offset === value) return;
      offset = value;
      markerUpdates.forEach(update => update());
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      this.clear();
    },
    set(loads) {
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
      renderedLines = display.lines.map(
        ({ points, role, loadId, routeColor, routeDepth, routeShared }) => {
          const line = new Polyline({
            map,
            routeRole: role,
            routeColor,
            routeDepth,
            routeShared,
            strokeWeight: 2,
            onHover: info => hover(loadId, !!info?.object),
          });
          line.setPath(
            points.map(p => ({ lat: p.latitude, lng: p.longitude })),
          );
          objects.push(line);
          return { line, loadId };
        },
      );
      for (const { stop, numbers, members, color } of display.groups) {
        const marker = new StopMarker({
          map,
          job: stop.job,
          position: { lat: stop.latitude, lng: stop.longitude },
          number: [...numbers].map(number => number + offset).join('/'),
          color,
          transientLabel: true,
          onHover: over => hover(identity(members[0]), over === true),
          onSelect: () => {
            if (disposed || !visible || previous !== signature) return;
            const current = members.findIndex(
              row =>
                identity(row) === selectedId && row.index === selectedStopIndex,
            );
            const row = members[(current + 1) % members.length];
            selectedId = identity(row);
            selectedStopIndex = row.index;
            applySelection();
            if (row.executionLegId)
              onSelection(row.loadId ?? null, row.index, row.executionLegId);
            else onSelection(row.loadId ?? null, row.index);
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
