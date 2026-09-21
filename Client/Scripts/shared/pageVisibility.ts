// Blazor asks to be told when the tab is hidden, so polling can stop.
export function observeVisibility(
  callback: {
    invokeMethodAsync(name: string, visible: boolean): Promise<void>;
  },
  document: Document = globalThis.document,
) {
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
