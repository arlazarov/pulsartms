// A strip that scrolls across: a mouse wheel's vertical turn moves it
// sideways while it has more to show that way, and the page and the map
// under the pointer do not take the turn. A trackpad's own sideways
// gesture, the keyboard and the scrollbar work as they always do.
export function bindHorizontalWheel(element: HTMLElement | null) {
  if (!element) return { dispose() {} };
  const onWheel = (event: WheelEvent) => {
    if (turnSideways(element, event) !== 'none') event.stopPropagation();
  };
  element.addEventListener('wheel', onWheel, { passive: false });
  return {
    dispose() {
      element.removeEventListener('wheel', onWheel);
    },
  };
}

// The same for every strip matching `selector` inside `root`, with one
// listener, so strips drawn again with a list keep it without binding
// each. A strip at its end lets the page scroll on down.
export function bindHorizontalWheelWithin(
  root: HTMLElement | null,
  selector: string,
) {
  if (!root) return { dispose() {} };
  const onWheel = (event: WheelEvent) => {
    const strip = (event.target as Element | null)?.closest(selector);
    if (strip instanceof HTMLElement && root.contains(strip))
      turnSideways(strip, event);
  };
  root.addEventListener('wheel', onWheel, { passive: false });
  return {
    dispose() {
      root.removeEventListener('wheel', onWheel);
    },
  };
}

// A vertical wheel turn moves the strip sideways: 'moved', 'edge' when the
// strip can scroll but is already at that end, 'none' when it cannot.
function turnSideways(element: HTMLElement, event: WheelEvent) {
  if (event.ctrlKey || Math.abs(event.deltaX) >= Math.abs(event.deltaY))
    return 'none';
  const room = element.scrollWidth - element.clientWidth;
  if (room <= 0) return 'none';
  const before = element.scrollLeft;
  const step =
    event.deltaMode === WheelEvent.DOM_DELTA_LINE
      ? event.deltaY * 16
      : event.deltaY;
  element.scrollLeft = Math.max(0, Math.min(room, before + step));
  if (element.scrollLeft === before) return 'edge';
  event.preventDefault();
  return 'moved';
}
