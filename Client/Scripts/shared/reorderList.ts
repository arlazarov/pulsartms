import type { DropTarget } from './reorderDrop.ts';
import { dropTarget, reorderRows, rowSelector } from './reorderDrop.ts';
// The drag in progress: which row is being carried, by which handle and
// pointer, from where, and whether it has yet moved far enough to count.
type Drag = {
  pointerId: number;
  handle: HTMLElement;
  source: HTMLElement;
  key: string;
  startX: number;
  startY: number;
  x: number;
  y: number;
  dragging: boolean;
};

const handleSelector = '[data-reorder-handle]';
const dragThreshold = 6;

export function attachReorderList(
  surface: HTMLElement,
  callbacks: {
    invokeMethodAsync(name: string, ...args: unknown[]): Promise<unknown>;
  },
) {
  const document = surface.ownerDocument;
  const view = document.defaultView!;
  let disposed = false;
  let active: Drag | null = null;
  let target: DropTarget | null = null;
  let frame: number | null = null;
  let suppressedClick: HTMLElement | null = null;

  function enabled(handle: HTMLElement) {
    return (
      !(handle as HTMLButtonElement).disabled &&
      !handle.matches(':disabled') &&
      handle.getAttribute('aria-disabled') !== 'true'
    );
  }

  function clearTarget() {
    target?.row.classList.remove('drop-before', 'drop-after');
    target = null;
  }

  function updateTarget() {
    clearTarget();
    if (!active?.dragging) return;
    target = dropTarget(surface, active.source, active.x, active.y);
    target?.row.classList.add(target.after ? 'drop-after' : 'drop-before');
  }

  function validSource() {
    return (
      active &&
      surface.isConnected &&
      surface.contains(active.source) &&
      active.source.dataset.reorderKey === active.key &&
      enabled(active.handle)
    );
  }

  function scrollFrame() {
    frame = null;
    if (!active?.dragging || !validSource()) {
      if (active) finish(false);
      return;
    }
    const bounds = surface.getBoundingClientRect();
    const speed = (point: number, start: number, end: number) =>
      point < start + 36
        ? -Math.min(12, (start + 36 - point) / 3)
        : point > end - 36
          ? Math.min(12, (point - end + 36) / 3)
          : 0;
    if (
      active.x >= bounds.left &&
      active.x <= bounds.right &&
      active.y >= bounds.top &&
      active.y <= bounds.bottom
    ) {
      const left = surface.scrollLeft;
      const top = surface.scrollTop;
      if (surface.scrollWidth > surface.clientWidth)
        surface.scrollLeft += speed(active.x, bounds.left, bounds.right);
      if (surface.scrollHeight > surface.clientHeight)
        surface.scrollTop += speed(active.y, bounds.top, bounds.bottom);
      if (surface.scrollLeft !== left || surface.scrollTop !== top) {
        updateTarget();
        frame = view.requestAnimationFrame(scrollFrame);
      }
    }
  }

  function consume(event: Event) {
    event.preventDefault();
    event.stopPropagation();
  }

  function onMove(event: PointerEvent) {
    if (!active || event.pointerId !== active.pointerId) return;
    consume(event);
    if (!validSource()) {
      finish(false);
      return;
    }
    active.x = event.clientX;
    active.y = event.clientY;
    if (
      !active.dragging &&
      Math.hypot(active.x - active.startX, active.y - active.startY) >=
        dragThreshold
    ) {
      active.dragging = true;
      active.source.classList.add('is-dragging');
    }
    updateTarget();
    if (active.dragging && frame === null)
      frame = view.requestAnimationFrame(scrollFrame);
  }

  function finish(commit: boolean) {
    const previous = active;
    if (!previous) return;
    const moved =
      commit && previous.dragging && target
        ? [previous.key, target.row.dataset.reorderKey, target.after]
        : null;
    active = null;
    clearTarget();
    previous.source.classList.remove('is-dragging');
    if (previous.dragging) suppressedClick = previous.handle;
    document.removeEventListener('pointermove', onMove as EventListener, true);
    document.removeEventListener('pointerup', onUp as EventListener, true);
    document.removeEventListener(
      'pointercancel',
      onCancel as EventListener,
      true,
    );
    document.removeEventListener('keydown', onKey as EventListener, true);
    previous.handle.removeEventListener(
      'lostpointercapture',
      onCancel as EventListener,
    );
    view.removeEventListener('blur', onBlur);
    if (frame !== null) view.cancelAnimationFrame(frame);
    frame = null;
    try {
      if (previous.handle.hasPointerCapture?.(previous.pointerId))
        previous.handle.releasePointerCapture(previous.pointerId);
    } catch {
      /* The browser may already have released a cancelled pointer. */
    }
    if (moved && !disposed) {
      Promise.resolve()
        .then(() => {
          if (!disposed)
            return callbacks.invokeMethodAsync('OnFuelStopMoved', ...moved);
        })
        .catch(() => {
          if (!disposed) console.warn('[Reorder list] Move callback failed.');
        });
    }
  }

  function onUp(event: PointerEvent) {
    if (!active || event.pointerId !== active.pointerId) return;
    if (active.dragging) consume(event);
    if (!validSource()) {
      finish(false);
      return;
    }
    active.x = event.clientX;
    active.y = event.clientY;
    updateTarget();
    finish(true);
  }

  function onCancel(event: PointerEvent) {
    if (active && event.pointerId === active.pointerId) finish(false);
  }

  function onBlur() {
    finish(false);
  }

  function onKey(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      consume(event);
      finish(false);
    }
  }

  function onDown(event: PointerEvent) {
    suppressedClick = null;
    if (disposed || active || event.button !== 0 || event.isPrimary === false)
      return;
    const handle = (event.target as Element)?.closest?.<HTMLElement>(
      handleSelector,
    );
    if (!handle || !surface.contains(handle) || !enabled(handle)) return;
    const source = handle.closest<HTMLElement>(rowSelector);
    if (
      !source ||
      !reorderRows(surface).includes(source) ||
      !source.dataset.reorderKey
    )
      return;
    consume(event);
    handle.focus({ preventScroll: true });
    active = {
      pointerId: event.pointerId,
      handle,
      source,
      key: source.dataset.reorderKey!,
      startX: event.clientX,
      startY: event.clientY,
      x: event.clientX,
      y: event.clientY,
      dragging: false,
    };
    document.addEventListener('pointermove', onMove as EventListener, {
      capture: true,
      passive: false,
    });
    document.addEventListener('pointerup', onUp as EventListener, true);
    document.addEventListener('pointercancel', onCancel as EventListener, true);
    document.addEventListener('keydown', onKey as EventListener, true);
    handle.addEventListener('lostpointercapture', onCancel as EventListener);
    view.addEventListener('blur', onBlur);
    try {
      handle.setPointerCapture?.(event.pointerId);
    } catch {
      /* Document listeners retain the drag outside its handle. */
    }
  }

  function onClick(event: MouseEvent) {
    if (event.detail !== 0 && suppressedClick?.contains(event.target as Node)) {
      consume(event);
      suppressedClick = null;
    }
  }

  function onHandleKey(event: KeyboardEvent) {
    if (
      !['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)
    )
      return;
    const handle = (event.target as Element)?.closest?.<HTMLElement>(
      handleSelector,
    );
    if (handle && surface.contains(handle) && enabled(handle))
      event.preventDefault();
  }

  surface.addEventListener('pointerdown', onDown as EventListener, {
    capture: true,
    passive: false,
  });
  surface.addEventListener('click', onClick, true);
  surface.addEventListener('keydown', onHandleKey, true);
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      finish(false);
      suppressedClick = null;
      surface.removeEventListener('pointerdown', onDown as EventListener, true);
      surface.removeEventListener('click', onClick, true);
      surface.removeEventListener('keydown', onHandleKey, true);
    },
  };
}
