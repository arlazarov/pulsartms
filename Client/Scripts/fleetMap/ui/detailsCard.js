// Fixed HTML details: no camera callbacks, frame loops or GPU work.
export function createDetailsCard(map, {onClose = () => {}} = {}) {
  const root = map.getDiv(), host = document.createElement('section');
  host.className = 'fleet-map-details-card';
  host.setAttribute('role', 'dialog');
  host.setAttribute('aria-label', 'Map details');
  const close = document.createElement('button');
  close.type = 'button'; close.className = 'fleet-map-details-card__close';
  close.setAttribute('aria-label', 'Close map details'); close.textContent = '×';
  const body = document.createElement('div');
  body.className = 'fleet-map-details-card__body';
  host.append(close, body);
  google.maps.OverlayView.preventMapHitsAndGesturesFrom(host);
  let content = null;
  let disposed = false;
  const api = {
    show(nextContent) {
      if (disposed) return;
      if (content !== nextContent) { content = nextContent; body.replaceChildren(content); }
      if (!host.isConnected) root.append(host);
    },
    hide() { host.remove(); },
    dispose() {
      if (disposed) return;
      disposed = true;
      host.remove();
      close.removeEventListener('click', dismiss);
      host.removeEventListener('keydown', onKeyDown);
      body.replaceChildren();
      content = null;
    },
  };
  function dismiss() {
    onClose();
    api.hide();
  }
  close.addEventListener('click', dismiss);
  function onKeyDown(event) { if (event.key === 'Escape') { event.stopPropagation(); dismiss(); } }
  host.addEventListener('keydown', onKeyDown);
  return api;
}
