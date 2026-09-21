// One thing the card can be showing, and the caller that put it there.
type Port = { onClose: () => void; disposed: boolean };

/**
 * The card docked under the map, whose content the page owns. Blazor owns
 * the persistent inspector shell; JavaScript owns only this empty host.
 *
 * @param onChange What the card is showing now, and which revision of it.
 * @param dismissMode What Escape leaves the card showing.
 */
export function createDockedDetails(
  host: HTMLElement | null,
  onChange: (kind: string, revision: number) => void = () => {},
  dismissMode: () => string = () => 'closed',
  focusTarget: { focus?: (options?: FocusOptions) => void } | null = null,
) {
  const ports = new Map<string, Port>();
  let owner: Port | null = null,
    mode = 'closed',
    revision = 0,
    content: Node | null = null,
    disposed = false;
  let suspended = false;

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
          show(nextContent: Node) {
            if (disposed || port.disposed || owner !== port) return;
            if (content !== nextContent) {
              content = nextContent;
              host?.replaceChildren(content);
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
