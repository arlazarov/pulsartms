import {show as showNative} from './cameraDialog.js';
import {lockScroll, unlockScroll} from './popup.js';

const dialogs = new WeakMap();

export function show(dialog) {
  if (!dialog?.isConnected || dialogs.has(dialog)) return;
  const opener = document.activeElement;
  const openerId = opener?.id;
  const openerControls = opener?.getAttribute?.('aria-controls');
  let locked = false;
  let startedOutside = false;
  const outside = event => {
    const rect = dialog.getBoundingClientRect();
    return event.target === dialog && (event.clientX < rect.left || event.clientX > rect.right
      || event.clientY < rect.top || event.clientY > rect.bottom);
  };
  const pointerDown = event => { startedOutside = outside(event); };
  const click = event => { if (startedOutside && outside(event)) close(dialog); startedOutside = false; };
  const release = () => {
    if (!locked) return;
    locked = false;
    unlockScroll();
    const target = opener?.isConnected ? opener : openerId ? document.getElementById(openerId)
      : openerControls ? [...document.querySelectorAll('[aria-controls]')].find(element => element.getAttribute('aria-controls') === openerControls) : null;
    if (target?.isConnected && typeof target.focus === 'function') target.focus({preventScroll: true});
  };
  dialog.addEventListener('pointerdown', pointerDown);
  dialog.addEventListener('click', click);
  dialog.addEventListener('close', release);
  dialogs.set(dialog, {pointerDown, click, release});
  try { lockScroll(); locked = true; showNative(dialog); }
  catch (error) { dispose(dialog); throw error; }
}

export function close(dialog) {
  if (dialog?.open) dialog.close();
}

export function dispose(dialog) {
  const state = dialogs.get(dialog);
  if (!state) return;
  close(dialog);
  state.release();
  dialog.removeEventListener('pointerdown', state.pointerDown);
  dialog.removeEventListener('click', state.click);
  dialog.removeEventListener('close', state.release);
  dialogs.delete(dialog);
}
