const rowSelector = '[data-reorder-key]';
const handleSelector = '[data-reorder-handle]';
const dragThreshold = 6;

export function attachReorderList(surface, callbacks) {
  const document = surface.ownerDocument;
  const view = document.defaultView;
  let disposed = false;
  let active = null;
  let target = null;
  let frame = null;
  let suppressedClick = null;

  function rows() {
    return [...surface.querySelectorAll(rowSelector)]
      .filter(row => row.closest('[data-reorder-surface]') === surface ||
        !row.closest('[data-reorder-surface]'));
  }

  function enabled(handle) {
    return !handle.disabled && !handle.matches(':disabled') && handle.getAttribute('aria-disabled') !== 'true';
  }

  function clearTarget() {
    target?.row.classList.remove('drop-before', 'drop-after');
    target = null;
  }

  function axis(items) {
    if (surface.dataset.reorderAxis === 'horizontal') return 'x';
    if (surface.dataset.reorderAxis === 'vertical') return 'y';
    const first = items[0].getBoundingClientRect();
    const last = items.at(-1).getBoundingClientRect();
    return Math.abs(last.left - first.left) > Math.abs(last.top - first.top) ? 'x' : 'y';
  }

  function updateTarget() {
    clearTarget();
    if (!active?.dragging) return;
    const bounds = surface.getBoundingClientRect();
    if (active.x < bounds.left || active.x > bounds.right || active.y < bounds.top || active.y > bounds.bottom) return;
    const items = rows();
    const sourceIndex = items.indexOf(active.source);
    if (sourceIndex < 0 || items.length < 2) return;
    const direction = axis(items);
    const point = direction === 'x' ? active.x : active.y;
    let nearest = null;
    for (const row of items) {
      const rect = row.getBoundingClientRect();
      const start = direction === 'x' ? rect.left : rect.top;
      const end = direction === 'x' ? rect.right : rect.bottom;
      const distance = Math.max(start - point, point - end, 0);
      if (!nearest || distance < nearest.distance) {
        nearest = {row, distance, after: row.dataset.reorderAfter !== 'false' && point >= (start + end) / 2};
      }
    }
    if (!nearest || nearest.row === active.source) return;
    const targetIndex = items.indexOf(nearest.row);
    const insertion = targetIndex + Number(nearest.after) - Number(targetIndex > sourceIndex);
    if (insertion === sourceIndex) return;
    target = nearest;
    target.row.classList.add(target.after ? 'drop-after' : 'drop-before');
  }

  function validSource() {
    return active && surface.isConnected && surface.contains(active.source) &&
      active.source.dataset.reorderKey === active.key && enabled(active.handle);
  }

  function scrollFrame() {
    frame = null;
    if (!active?.dragging || !validSource()) {
      if (active) finish(false);
      return;
    }
    const bounds = surface.getBoundingClientRect();
    const speed = (point, start, end) => point < start + 36 ? -Math.min(12, (start + 36 - point) / 3)
      : point > end - 36 ? Math.min(12, (point - end + 36) / 3) : 0;
    if (active.x >= bounds.left && active.x <= bounds.right && active.y >= bounds.top && active.y <= bounds.bottom) {
      const left = surface.scrollLeft;
      const top = surface.scrollTop;
      if (surface.scrollWidth > surface.clientWidth) surface.scrollLeft += speed(active.x, bounds.left, bounds.right);
      if (surface.scrollHeight > surface.clientHeight) surface.scrollTop += speed(active.y, bounds.top, bounds.bottom);
      if (surface.scrollLeft !== left || surface.scrollTop !== top) {
        updateTarget();
        frame = view.requestAnimationFrame(scrollFrame);
      }
    }
  }

  function consume(event) {
    event.preventDefault();
    event.stopPropagation();
  }

  function onMove(event) {
    if (!active || event.pointerId !== active.pointerId) return;
    consume(event);
    if (!validSource()) { finish(false); return; }
    active.x = event.clientX;
    active.y = event.clientY;
    if (!active.dragging && Math.hypot(active.x - active.startX, active.y - active.startY) >= dragThreshold) {
      active.dragging = true;
      active.source.classList.add('is-dragging');
    }
    updateTarget();
    if (active.dragging && frame === null) frame = view.requestAnimationFrame(scrollFrame);
  }

  function finish(commit) {
    const previous = active;
    if (!previous) return;
    const moved = commit && previous.dragging && target
      ? [previous.key, target.row.dataset.reorderKey, target.after] : null;
    active = null;
    clearTarget();
    previous.source.classList.remove('is-dragging');
    if (previous.dragging) suppressedClick = previous.handle;
    document.removeEventListener('pointermove', onMove, true);
    document.removeEventListener('pointerup', onUp, true);
    document.removeEventListener('pointercancel', onCancel, true);
    document.removeEventListener('keydown', onKey, true);
    previous.handle.removeEventListener('lostpointercapture', onCancel);
    view.removeEventListener('blur', onBlur);
    if (frame !== null) view.cancelAnimationFrame(frame);
    frame = null;
    try {
      if (previous.handle.hasPointerCapture?.(previous.pointerId)) previous.handle.releasePointerCapture(previous.pointerId);
    } catch { /* The browser may already have released a cancelled pointer. */ }
    if (moved && !disposed) {
      Promise.resolve().then(() => {
        if (!disposed) return callbacks.invokeMethodAsync('OnFuelStopMoved', ...moved);
      }).catch(() => {
        if (!disposed) console.warn('[Reorder list] Move callback failed.');
      });
    }
  }

  function onUp(event) {
    if (!active || event.pointerId !== active.pointerId) return;
    if (active.dragging) consume(event);
    if (!validSource()) { finish(false); return; }
    active.x = event.clientX;
    active.y = event.clientY;
    updateTarget();
    finish(true);
  }

  function onCancel(event) {
    if (active && event.pointerId === active.pointerId) finish(false);
  }

  function onBlur() { finish(false); }

  function onKey(event) {
    if (event.key === 'Escape') { consume(event); finish(false); }
  }

  function onDown(event) {
    suppressedClick = null;
    if (disposed || active || event.button !== 0 || event.isPrimary === false) return;
    const handle = event.target.closest?.(handleSelector);
    if (!handle || !surface.contains(handle) || !enabled(handle)) return;
    const source = handle.closest(rowSelector);
    if (!source || !rows().includes(source) || !source.dataset.reorderKey) return;
    consume(event);
    handle.focus({preventScroll: true});
    active = {pointerId: event.pointerId, handle, source, key: source.dataset.reorderKey,
      startX: event.clientX, startY: event.clientY, x: event.clientX, y: event.clientY, dragging: false};
    document.addEventListener('pointermove', onMove, {capture: true, passive: false});
    document.addEventListener('pointerup', onUp, true);
    document.addEventListener('pointercancel', onCancel, true);
    document.addEventListener('keydown', onKey, true);
    handle.addEventListener('lostpointercapture', onCancel);
    view.addEventListener('blur', onBlur);
    try { handle.setPointerCapture?.(event.pointerId); } catch { /* Document listeners retain the drag outside its handle. */ }
  }

  function onClick(event) {
    if (event.detail !== 0 && suppressedClick?.contains(event.target)) {
      consume(event);
      suppressedClick = null;
    }
  }

  function onHandleKey(event) {
    if (!['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)) return;
    const handle = event.target.closest?.(handleSelector);
    if (handle && surface.contains(handle) && enabled(handle)) event.preventDefault();
  }

  surface.addEventListener('pointerdown', onDown, {capture: true, passive: false});
  surface.addEventListener('click', onClick, true);
  surface.addEventListener('keydown', onHandleKey, true);
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      finish(false);
      suppressedClick = null;
      surface.removeEventListener('pointerdown', onDown, true);
      surface.removeEventListener('click', onClick, true);
      surface.removeEventListener('keydown', onHandleKey, true);
    },
  };
}
