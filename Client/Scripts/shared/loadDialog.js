import { show as showNative } from './cameraDialog.js';
import { lockScroll, unlockScroll } from './popup.js';

const dialogs = new WeakMap();
const overviews = new WeakMap();

export function rememberOverview(dialog) {
  const content = dialog.querySelector('.dispatch-load-dialog__content');
  overviews.set(dialog, {
    top: content.scrollTop,
    opener: dialog.ownerDocument.activeElement,
  });
  dialog.style.height = `${dialog.getBoundingClientRect().height}px`;
}

export function focusView(dialog, selected) {
  const content = dialog.querySelector('.dispatch-load-dialog__content');
  if (selected) {
    content.scrollTop = 0;
    dialog
      .querySelector('.dispatch-load-dialog__back')
      ?.focus({ preventScroll: true });
  } else {
    dialog.style.height = '';
    const previous = overviews.get(dialog);
    content.scrollTop = previous?.top ?? 0;
    if (previous?.opener?.isConnected)
      previous.opener.focus({ preventScroll: true });
    overviews.delete(dialog);
  }
}

export function show(dialog) {
  if (!dialog?.isConnected || dialogs.has(dialog)) return;
  const opener = document.activeElement;
  const openerId = opener?.id;
  const openerControls = opener?.getAttribute?.('aria-controls');
  let locked = false;
  let startedOutside = false;
  const outside = event => {
    const rect = dialog.getBoundingClientRect();
    return (
      event.target === dialog &&
      (event.clientX < rect.left ||
        event.clientX > rect.right ||
        event.clientY < rect.top ||
        event.clientY > rect.bottom)
    );
  };
  const pointerDown = event => {
    startedOutside = outside(event);
  };
  const click = event => {
    if (startedOutside && outside(event)) close(dialog);
    startedOutside = false;
  };
  const release = () => {
    if (!locked) return;
    locked = false;
    unlockScroll();
    const target = opener?.isConnected
      ? opener
      : openerId
        ? document.getElementById(openerId)
        : openerControls
          ? [...document.querySelectorAll('[aria-controls]')].find(
              element =>
                element.getAttribute('aria-controls') === openerControls,
            )
          : null;
    if (target?.isConnected && typeof target.focus === 'function')
      target.focus({ preventScroll: true });
  };
  dialog.addEventListener('pointerdown', pointerDown);
  dialog.addEventListener('click', click);
  dialog.addEventListener('close', release);
  dialogs.set(dialog, { pointerDown, click, release });
  try {
    lockScroll();
    locked = true;
    showNative(dialog);
  } catch (error) {
    dispose(dialog);
    throw error;
  }
}

export function close(dialog) {
  if (dialog?.open) dialog.close();
}

export function dispose(dialog) {
  overviews.delete(dialog);
  const state = dialogs.get(dialog);
  if (!state) return;
  close(dialog);
  state.release();
  dialog.removeEventListener('pointerdown', state.pointerDown);
  dialog.removeEventListener('click', state.click);
  dialog.removeEventListener('close', state.release);
  dialogs.delete(dialog);
}
