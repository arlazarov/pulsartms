import { nextLoadDisplay, nextLoadKey } from './nextLoadDisplay.js';

export function createNextLoadsLayer(
  map,
  Polyline,
  StopMarker,
  onSelection = () => {},
  reveal = () => {},
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
  // Where each load is on the ground: its roads and the stops at their ends.
  let loadGeometry = new Map();
  let loadMembers = new Map();
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
  // A load picked on the map is a load the dispatcher wants to see. Its
  // badges stand at its own pickup and delivery, which can be several hundred
  // miles from the truck - so picking the road highlighted nothing but the
  // road, and the circles it was picked for were off the screen entirely.
  function select(member, loadId) {
    if (disposed || !visible || !member) return;
    selectedId = identity(member);
    selectedStopIndex = member.index;
    applySelection();
    reveal(loadGeometry.get(loadId ?? selectedId) ?? null);
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
      const remember = (loadId, point) => {
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
            onHover: info => hover(loadId, !!info?.object),
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
          onHover: over => hover(identity(members[0]), over === true),
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
