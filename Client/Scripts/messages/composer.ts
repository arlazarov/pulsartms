// The Messages conversation's reply box and its drop zone. Enter sends;
// Option (Alt) or Shift with Enter starts a new line; a key an input
// method is still composing is left to it. Files dropped anywhere on the
// conversation go to its file input, as if picked with the paperclip, so
// Blazor stages them the same way.

export function attach(root: HTMLElement) {
  const onKey = (event: KeyboardEvent) => {
    const box = event.target;
    if (
      !(box instanceof HTMLTextAreaElement) ||
      !box.hasAttribute('data-send-on-enter') ||
      event.key !== 'Enter' ||
      event.isComposing ||
      event.keyCode === 229
    )
      return;
    if (event.shiftKey) return;
    event.preventDefault();
    if (event.altKey) {
      box.setRangeText('\n', box.selectionStart, box.selectionEnd, 'end');
      box.dispatchEvent(new Event('input', { bubbles: true }));
      return;
    }
    if (!event.ctrlKey && !event.metaKey && box.value.trim() === '') {
      // Enter in an empty box still sends staged files.
      if (!box.form?.querySelector('[data-staged]')) return;
    }
    box.form?.requestSubmit();
  };

  const input = () =>
    root.querySelector<HTMLInputElement>('input[type=file][data-drop-target]');
  const carriesFiles = (event: DragEvent) =>
    event.dataTransfer?.types.includes('Files') ?? false;
  const onOver = (event: DragEvent) => {
    if (!carriesFiles(event)) return;
    event.preventDefault();
    const target = input();
    if (event.dataTransfer)
      event.dataTransfer.dropEffect =
        target && !target.disabled ? 'copy' : 'none';
    root.classList.toggle('is-dropping', !!target && !target.disabled);
  };
  const onLeave = (event: DragEvent) => {
    if (!root.contains(event.relatedTarget as Node | null))
      root.classList.remove('is-dropping');
  };
  const onDrop = (event: DragEvent) => {
    root.classList.remove('is-dropping');
    if (!carriesFiles(event)) return;
    event.preventDefault();
    const target = input();
    if (!target || target.disabled || !event.dataTransfer?.files.length) return;
    target.files = event.dataTransfer.files;
    target.dispatchEvent(new Event('change', { bubbles: true }));
  };

  root.addEventListener('keydown', onKey);
  root.addEventListener('dragover', onOver);
  root.addEventListener('dragleave', onLeave);
  root.addEventListener('drop', onDrop);
  return {
    dispose() {
      root.removeEventListener('keydown', onKey);
      root.removeEventListener('dragover', onOver);
      root.removeEventListener('dragleave', onLeave);
      root.removeEventListener('drop', onDrop);
      root.classList.remove('is-dropping');
    },
  };
}
