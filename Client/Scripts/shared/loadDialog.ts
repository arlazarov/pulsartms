import { show as showNative } from './cameraDialog.ts';
import { lockScroll, unlockScroll } from './popup.ts';

// What a dialog listens with while it is open, so it can all be taken off
// again, and where its overview stood before a stop was opened over it.
type Listeners = {
  pointerDown: (event: PointerEvent) => void;
  click: (event: MouseEvent) => void;
  release: () => void;
};
type Overview = { top: number; opener: Element | null };

const dialogs = new WeakMap<HTMLDialogElement, Listeners>();
const overviews = new WeakMap<HTMLDialogElement, Overview>();

// What can be focused is asked of the element, not of its type: the pages
// are also rendered in a test host whose elements are not the browser's.
function focusElement(element: Element | null | undefined) {
  const focusable = element as {
    focus?: (options?: FocusOptions) => void;
  } | null;
  if (typeof focusable?.focus === 'function')
    focusable.focus({ preventScroll: true });
}

export function rememberOverview(dialog: HTMLDialogElement) {
  const content = dialog.querySelector('.dispatch-load-dialog__content')!;
  overviews.set(dialog, {
    top: content.scrollTop,
    opener: dialog.ownerDocument.activeElement,
  });
  dialog.style.height = `${dialog.getBoundingClientRect().height}px`;
}

export function focusView(dialog: HTMLDialogElement, selected: boolean) {
  const content = dialog.querySelector('.dispatch-load-dialog__content')!;
  if (selected) {
    content.scrollTop = 0;
    focusElement(dialog.querySelector('.dispatch-load-dialog__back'));
  } else {
    dialog.style.height = '';
    const previous = overviews.get(dialog);
    content.scrollTop = previous?.top ?? 0;
    if (previous?.opener?.isConnected) focusElement(previous.opener);
    overviews.delete(dialog);
  }
}

export function show(dialog: HTMLDialogElement | null | undefined) {
  if (!dialog?.isConnected || dialogs.has(dialog)) return;
  const opener = document.activeElement;
  const openerId = opener?.id;
  const openerControls = opener?.getAttribute?.('aria-controls');
  let locked = false;
  let startedOutside = false;
  const outside = (event: MouseEvent) => {
    const rect = dialog.getBoundingClientRect();
    return (
      event.target === dialog &&
      (event.clientX < rect.left ||
        event.clientX > rect.right ||
        event.clientY < rect.top ||
        event.clientY > rect.bottom)
    );
  };
  const pointerDown = (event: PointerEvent) => {
    startedOutside = outside(event);
  };
  const click = (event: MouseEvent) => {
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
    if (target?.isConnected) focusElement(target);
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

export function close(dialog: HTMLDialogElement | null | undefined) {
  if (dialog?.open) dialog.close();
}

export function dispose(dialog: HTMLDialogElement) {
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
