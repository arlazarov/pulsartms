// @ts-check
/** @param {HTMLDialogElement | null | undefined} dialog */
export function show(dialog) {
  if (dialog?.isConnected && !dialog.open) dialog.showModal();
}
