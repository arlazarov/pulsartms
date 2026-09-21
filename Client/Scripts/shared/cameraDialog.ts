// The page owns the dialog; it is opened here so that the browser's own
// modal behaviour - the backdrop, Escape, the focus trap - is the one in use.
export function show(dialog: HTMLDialogElement | null | undefined) {
  if (dialog?.isConnected && !dialog.open) dialog.showModal();
}
