// A row that can be carried says so by carrying its key.
export const rowSelector = '[data-reorder-key]';

// Where a carried row would land: the row it would land beside, how far
// that row is from the pointer, and whether it would go after or before it.
export type DropTarget = { row: HTMLElement; distance: number; after: boolean };

// The rows of this surface. A row inside a nested surface belongs to that
// one, not to this.
export function reorderRows(surface: HTMLElement): HTMLElement[] {
  return [...surface.querySelectorAll<HTMLElement>(rowSelector)].filter(
    row =>
      row.closest('[data-reorder-surface]') === surface ||
      !row.closest('[data-reorder-surface]'),
  );
}

// Which way the rows run. The surface may say; otherwise it is read off the
// distance between the first row and the last.
function axis(surface: HTMLElement, items: HTMLElement[]): 'x' | 'y' {
  if (surface.dataset.reorderAxis === 'horizontal') return 'x';
  if (surface.dataset.reorderAxis === 'vertical') return 'y';
  const first = items[0].getBoundingClientRect();
  const last = items.at(-1)!.getBoundingClientRect();
  return Math.abs(last.left - first.left) > Math.abs(last.top - first.top)
    ? 'x'
    : 'y';
}

/**
 * Where the carried row would land if it were let go at this point, or
 * nothing at all: off the surface, alone on it, or back where it started -
 * a move that changes no order is not a move.
 */
export function dropTarget(
  surface: HTMLElement,
  source: HTMLElement,
  x: number,
  y: number,
): DropTarget | null {
  const bounds = surface.getBoundingClientRect();
  if (
    x < bounds.left ||
    x > bounds.right ||
    y < bounds.top ||
    y > bounds.bottom
  )
    return null;
  const items = reorderRows(surface);
  const sourceIndex = items.indexOf(source);
  if (sourceIndex < 0 || items.length < 2) return null;
  const direction = axis(surface, items);
  const point = direction === 'x' ? x : y;
  let nearest: DropTarget | null = null;
  for (const row of items) {
    const rect = row.getBoundingClientRect();
    const start = direction === 'x' ? rect.left : rect.top;
    const end = direction === 'x' ? rect.right : rect.bottom;
    const distance = Math.max(start - point, point - end, 0);
    if (!nearest || distance < nearest.distance) {
      nearest = {
        row,
        distance,
        after:
          row.dataset.reorderAfter !== 'false' && point >= (start + end) / 2,
      };
    }
  }
  if (!nearest || nearest.row === source) return null;
  const targetIndex = items.indexOf(nearest.row);
  const insertion =
    targetIndex + Number(nearest.after) - Number(targetIndex > sourceIndex);
  return insertion === sourceIndex ? null : nearest;
}
