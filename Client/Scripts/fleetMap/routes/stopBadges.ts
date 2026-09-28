// What a stop's badge says on the map (the owner, September 28): its
// place in the chosen truck's trip chain, a plain 1, 2, 3, with no
// letter or icon. The page owns the
// numbers - it reads the chain the trip cards show and sends one per stop
// (FleetMap's PushStopBadgesAsync, from Shared/Dispatch/StopMarkers
// .ChainBadges) - so the map and the cards cannot disagree, and choosing a
// stop or a layer renumbers nothing. A stop outside that chain (another
// truck's load, a route preview) owns no number: its badge is plain.
const badges = new Map<string, string>();
const listeners = new Set<() => void>();

export function setStopBadges(record: unknown) {
  const next = new Map<string, string>();
  if (record && typeof record === 'object')
    for (const [id, label] of Object.entries(record))
      if (typeof label === 'string' && label) next.set(id, label);
  if (
    next.size === badges.size &&
    [...next].every(([id, label]) => badges.get(id) === label)
  )
    return;
  badges.clear();
  for (const [id, label] of next) badges.set(id, label);
  for (const listener of [...listeners]) listener();
}

export function stopBadge(id: unknown, _job?: unknown) {
  return (typeof id === 'string' && badges.get(id)) || '';
}

export function onStopBadgesChanged(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
