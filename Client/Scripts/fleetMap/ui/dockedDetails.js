// Blazor owns the persistent inspector shell; JavaScript owns only this empty host.
export function createDockedDetails(host, onChange = () => {}, dismissMode = () => 'closed', focusTarget = null) {
  const ports = new Map();
  let owner = null, mode = 'closed', revision = 0, content = null, disposed = false;

  function clear() {
    if (content !== null) host?.replaceChildren();
    content = null;
  }

  function switchMode(next, nextOwner = null, restoreFocus = false) {
    if (disposed) return;
    if (restoreFocus && host?.closest?.('.fleet-map-inspector')?.contains(host.ownerDocument?.activeElement))
      focusTarget?.focus?.({ preventScroll: true });
    owner = nextOwner;
    mode = next;
    clear();
    onChange(mode, ++revision);
  }

  function dismiss(event) {
    if (event.key !== 'Escape' || !owner) return;
    event.stopPropagation();
    const closing = owner;
    switchMode(dismissMode(), null, true);
    closing.onClose();
  }
  host?.addEventListener('keydown', dismiss);

  return {
    get mode() { return mode; },
    activate(kind) {
      const port = ports.get(kind);
      if (port && !port.disposed) switchMode(kind, port);
    },
    setMode(kind, restoreFocus = false) { switchMode(kind, null, restoreFocus); },
    popupFactory(kind) {
      return (_map, {onClose = () => {}} = {}) => {
        const port = {onClose, disposed: false};
        ports.set(kind, port);
        return {
          show(nextContent) {
            if (disposed || port.disposed || owner !== port) return;
            if (content !== nextContent) { content = nextContent; host?.replaceChildren(content); }
          },
          hide() { if (!disposed && owner === port) switchMode('closed'); },
          dispose() {
            if (port.disposed) return;
            port.disposed = true;
            if (owner === port) { owner = null; mode = 'closed'; clear(); }
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
