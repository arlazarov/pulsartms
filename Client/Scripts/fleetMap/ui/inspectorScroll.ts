// The map's card is one scrolling box whatever it shows, so a scroll made
// in one card (a long stop) was still there in the next: back at the
// truck, its head sat above the box's top (Dispatch, September 28). Each
// card opens from its head: when what the card shows changes, its scroll
// goes back to the top. A rerender of the same card keeps the reader's
// place. Focus is not moved.
export function watchInspectorScroll(stage: ParentNode | null | undefined) {
  const inspector = stage?.querySelector<HTMLElement>(
    '.fleet-map-info-reserved',
  );
  const Mutation = inspector?.ownerDocument?.defaultView?.MutationObserver;
  if (!inspector || typeof Mutation !== 'function') return () => {};
  const observer = new Mutation(records => {
    const mode = inspector.getAttribute('data-inspector-mode');
    if (records.some(record => record.oldValue !== mode))
      inspector.scrollTop = 0;
  });
  observer.observe(inspector, {
    attributes: true,
    attributeFilter: ['data-inspector-mode'],
    attributeOldValue: true,
  });
  return () => observer.disconnect();
}
