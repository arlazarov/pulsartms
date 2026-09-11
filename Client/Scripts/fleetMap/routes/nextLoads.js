import { nextLoadDisplay } from './nextLoadDisplay.js';

export function createNextLoadsLayer(map, Polyline, StopMarker, onSelection = () => {}) {
  const objects = [];
  const markerUpdates = [];
  let previous = null;
  let disposed = false;
  let offset = 0;
  let cachedLoads = null;
  let visible = true;
  let selectedId = null;
  let selectedStopIndex = null;
  let markerGroups = [];
  let renderedLines = [];
  const identity = row => row.loadId ?? row.loadNumber;

  function applySelection() {
    for (const group of markerGroups)
      group.marker.highlighted = group.members.some(row => identity(row) === selectedId);
    for (const { line, loadId } of renderedLines)
      line?.setOptions({ strokeWeight: 2, zIndex: loadId === selectedId ? 10 : 0,
        routeSelected: selectedId !== null && loadId === selectedId,
        routeMuted: selectedId !== null && loadId !== selectedId });
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
      if (!loads.some(load => (load.id ?? load.loadNumber) === selectedId)) clearSelection();
      if (!visible) { clearObjects(); return; }
      const signature = JSON.stringify(loads);
      if (signature === previous) return;
      clearObjects();
      previous = signature;
      const display = nextLoadDisplay(loads);
      renderedLines = display.lines.map(({ points, role, loadId, routeColor }) => {
        const line = new Polyline({ map, routeRole: role, routeColor, strokeWeight: 2 });
        line.setPath(points.map(p => ({ lat: p.latitude, lng: p.longitude })));
        objects.push(line);
        return { line, loadId };
      });
      for (const { stop, numbers, members, color } of display.groups) {
        const marker = new StopMarker({ map, job: stop.job,
          position: { lat: stop.latitude, lng: stop.longitude },
          number: [...numbers].map(number => number + offset).join('/'), color, transientLabel: true,
          onSelect: () => {
            if (disposed || !visible || previous !== signature) return;
            const current = members.findIndex(row => identity(row) === selectedId && row.index === selectedStopIndex);
            const row = members[(current + 1) % members.length];
            selectedId = identity(row);
            selectedStopIndex = row.index;
            applySelection();
            onSelection(row.loadId ?? null, row.index);
          } });
        markerUpdates.push(() => marker.setNumber([...numbers].map(number => number + offset).join('/')));
        markerGroups.push({ marker, members });
        objects.push(marker);
      }
      applySelection();
    },
  };
}
