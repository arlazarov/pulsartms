// Reports document visibility to Blazor so polling can slow down in hidden tabs.
export function watch(receiver) {
  const notify = () => receiver.invokeMethodAsync('OnVisibilityChanged', document.visibilityState === 'hidden');
  document.addEventListener('visibilitychange', notify);
  notify();
  return {dispose() { document.removeEventListener('visibilitychange', notify); }};
}
