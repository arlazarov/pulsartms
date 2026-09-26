// One thing the card can be showing, and the caller that put it there.
type Port = { onClose: () => void; disposed: boolean };

// A position as a popup names it - the provider's LatLng or a literal -
// read as numbers; anything else is no position.
function literal(value: unknown): google.maps.LatLngLiteral | null {
  if (!value || typeof value !== 'object') return null;
  const at = value as { lat?: unknown; lng?: unknown };
  const lat = typeof at.lat === 'function' ? at.lat() : at.lat,
    lng = typeof at.lng === 'function' ? at.lng() : at.lng;
  return typeof lat === 'number' && typeof lng === 'number'
    ? { lat, lng }
    : null;
}

/**
 * The card docked under the map, whose content the page owns. Blazor owns
 * the persistent inspector shell; JavaScript owns only this empty host.
 *
 * @param onChange What the card is showing now, and which revision of it.
 * @param dismissMode What Escape leaves the card showing.
 * @param onShow Where the thing the card now shows stands on the map: said
 *   once per place, not again when the same stop's content is refreshed.
 */
export function createDockedDetails(
  host: HTMLElement | null,
  onChange: (kind: string, revision: number) => void = () => {},
  dismissMode: () => string = () => 'closed',
  focusTarget: { focus?: (options?: FocusOptions) => void } | null = null,
  onShow: (position: google.maps.LatLngLiteral) => void = () => {},
) {
  const ports = new Map<string, Port>();
  let owner: Port | null = null,
    mode = 'closed',
    revision = 0,
    content: Node | null = null,
    disposed = false;
  let suspended = false;
  // Where what the card shows stands, said once per activation: the same
  // stop opened again is a new pick, its content refreshed is not.
  let shownAt = '';

  function clear() {
    if (content !== null) host?.replaceChildren();
    content = null;
  }

  function switchMode(
    next: string,
    nextOwner: Port | null = null,
    restoreFocus = false,
  ) {
    if (disposed || (suspended && next !== 'closed')) return;
    if (
      restoreFocus &&
      host
        ?.closest?.('.fleet-map-inspector')
        ?.contains(host.ownerDocument?.activeElement)
    )
      focusTarget?.focus?.({ preventScroll: true });
    owner = nextOwner;
    mode = next;
    shownAt = '';
    clear();
    onChange(mode, ++revision);
  }

  function dismiss(event: KeyboardEvent) {
    if (event.key !== 'Escape' || !owner) return;
    event.stopPropagation();
    const closing = owner;
    switchMode(dismissMode(), null, true);
    closing.onClose();
  }
  host?.addEventListener('keydown', dismiss);

  return {
    get mode() {
      return mode;
    },
    get revision() {
      return revision;
    },
    get suspended() {
      return suspended;
    },
    setSuspended(value: boolean) {
      if (disposed || suspended === value) return;
      suspended = value;
      if (suspended) switchMode('closed');
    },
    activate(kind: string) {
      const port = ports.get(kind);
      if (port && !port.disposed) switchMode(kind, port);
    },
    setMode(kind: string, restoreFocus = false) {
      switchMode(kind, null, restoreFocus);
    },
    popupFactory(kind: string) {
      return (
        _map: unknown,
        { onClose = () => {} }: { onClose?: () => void } = {},
      ) => {
        const port: Port = { onClose, disposed: false };
        ports.set(kind, port);
        return {
          show(nextContent: Node, position?: unknown) {
            if (disposed || port.disposed || owner !== port) return;
            if (content !== nextContent) {
              content = nextContent;
              host?.replaceChildren(content);
            }
            const place = literal(position);
            const key = place ? `${place.lat}:${place.lng}` : '';
            if (place && key !== shownAt) {
              shownAt = key;
              onShow(place);
            }
          },
          hide() {
            if (!disposed && owner === port) switchMode('closed');
          },
          dispose() {
            if (port.disposed) return;
            port.disposed = true;
            if (owner === port) {
              owner = null;
              mode = 'closed';
              clear();
            }
            if (ports.get(kind) === port) ports.delete(kind);
          },
        };
      };
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      owner = null;
      ports.clear();
      clear();
      host?.removeEventListener('keydown', dismiss);
    },
  };
}
