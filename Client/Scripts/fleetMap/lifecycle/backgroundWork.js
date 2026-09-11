// Network promises alone do not let the browser paint between CPU-heavy stages.
export function yieldToBrowser() {
  if (globalThis.scheduler?.postTask)
    return globalThis.scheduler.postTask(() => {}, { priority: 'background' });
  return new Promise(resolve => setTimeout(resolve, 0));
}
