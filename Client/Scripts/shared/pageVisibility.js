export function observeVisibility(callback, document = globalThis.document) {
  let disposed = false;
  const changed = () => {
    if (!disposed)
      callback
        .invokeMethodAsync('VisibilityChanged', !document.hidden)
        .catch(() => {});
  };
  document.addEventListener('visibilitychange', changed);
  return {
    isVisible: () => !document.hidden,
    dispose() {
      disposed = true;
      document.removeEventListener('visibilitychange', changed);
    },
  };
}
