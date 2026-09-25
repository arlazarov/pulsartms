// The Messages conversation's scrolling. The scroller is reversed in CSS,
// so it opens at the newest message and older pages added above do not
// move what is read. This keeps the rest steady: while the reader is away
// from the newest message, a message arriving below or a photo settling
// its size moves nothing on screen; near the newest it stays in view.
// Near the top it asks for the next older page; the page decides whether
// one is due, and asks one at a time.

interface Callbacks {
  invokeMethodAsync(name: string): Promise<unknown>;
}

// How close to an end counts as being there, in pixels.
const nearEnd = 96;
const topFor = 400;

export function attach(scroller: HTMLElement, page: Callbacks) {
  // Reversed scrollers count scrollTop from the bottom, as zero or less.
  const fromBottom = () =>
    scroller.scrollTop <= 0
      ? -scroller.scrollTop
      : scroller.scrollHeight - scroller.clientHeight - scroller.scrollTop;
  const fromTop = () =>
    scroller.scrollHeight - scroller.clientHeight - fromBottom();

  let atBottom = true;
  let anchor: { id: string; top: number } | null = null;

  const remember = () => {
    const box = scroller.getBoundingClientRect();
    anchor = null;
    for (const item of scroller.querySelectorAll<HTMLElement>(
      '[data-message-id]',
    )) {
      const rect = item.getBoundingClientRect();
      if (rect.bottom > box.top) {
        anchor = { id: item.dataset.messageId!, top: rect.top - box.top };
        return;
      }
    }
  };

  const restore = () => {
    if (atBottom || !anchor) return;
    const item = scroller.querySelector<HTMLElement>(
      `[data-message-id="${CSS.escape(anchor.id)}"]`,
    );
    if (!item) return;
    const moved =
      item.getBoundingClientRect().top -
      scroller.getBoundingClientRect().top -
      anchor.top;
    if (Math.abs(moved) >= 1) scroller.scrollTop += moved;
  };

  const check = () => {
    const bottom = fromBottom() <= nearEnd;
    if (bottom && !atBottom) void page.invokeMethodAsync('AtNewest');
    atBottom = bottom;
    remember();
    if (fromTop() <= topFor) void page.invokeMethodAsync('NearOldest');
  };

  // Content changed size: new messages, an older page, a photo loaded.
  const resized = new ResizeObserver(() => {
    restore();
    check();
  });
  const observe = () => {
    resized.disconnect();
    for (const child of scroller.children) resized.observe(child);
  };
  const children = new MutationObserver(observe);
  children.observe(scroller, { childList: true });
  observe();
  scroller.addEventListener('scroll', check, { passive: true });
  check();

  return {
    isNearNewest: () => fromBottom() <= nearEnd,
    toNewest() {
      scroller.scrollTop = scroller.scrollTop <= 0 ? 0 : scroller.scrollHeight;
      atBottom = true;
      remember();
    },
    // A search result: brought into view and marked for a moment.
    reveal(id: string) {
      const item = scroller.querySelector<HTMLElement>(
        `[data-message-id="${CSS.escape(id)}"]`,
      );
      if (!item) return;
      item.scrollIntoView({ block: 'center' });
      item.classList.add('is-found');
      atBottom = fromBottom() <= nearEnd;
      remember();
    },
    dispose() {
      resized.disconnect();
      children.disconnect();
      scroller.removeEventListener('scroll', check);
    },
  };
}
