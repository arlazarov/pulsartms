// A strip that scrolls across: a mouse wheel's vertical turn moves it
// sideways while it has more to show that way, and the page and the map
// under the pointer do not take the turn. A trackpad's own sideways
// gesture, the keyboard and the scrollbar work as they always do.
export function bindHorizontalWheel(element: HTMLElement | null) {
  if (!element) return { dispose() {} };
  const onWheel = (event: WheelEvent) => {
    if (event.ctrlKey || Math.abs(event.deltaX) >= Math.abs(event.deltaY))
      return;
    const room = element.scrollWidth - element.clientWidth;
    if (room <= 0) return;
    const before = element.scrollLeft;
    const step =
      event.deltaMode === WheelEvent.DOM_DELTA_LINE
        ? event.deltaY * 16
        : event.deltaY;
    element.scrollLeft = Math.max(0, Math.min(room, before + step));
    if (element.scrollLeft !== before) event.preventDefault();
    event.stopPropagation();
  };
  element.addEventListener('wheel', onWheel, { passive: false });
  return {
    dispose() {
      element.removeEventListener('wheel', onWheel);
    },
  };
}
