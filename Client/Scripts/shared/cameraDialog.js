export function show(dialog) {
  if (dialog?.isConnected && !dialog.open) dialog.showModal();
}
