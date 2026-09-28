// The theme on the page, and remembered in this browser. While dark is the
// only theme (the owner, September 28) index.html does not read it and
// the app applies dark; light stays supported here for its return.
export function applyTheme(theme: string): void {
  const value = theme === 'dark' ? 'dark' : 'light';
  const root = document.documentElement;
  const apply = () => {
    root.dataset.theme = value;
  };
  // A switch cross-fades the page rather than snapping every colour at
  // once; the first paint and an unchanged theme just apply.
  const doc = document as Document & {
    startViewTransition?: (update: () => void) => unknown;
  };
  if (
    root.dataset.theme &&
    root.dataset.theme !== value &&
    doc.startViewTransition
  )
    doc.startViewTransition(apply);
  else apply();
  try {
    localStorage.setItem('pulsr.theme', value);
  } catch {
    // A browser that refuses storage paints dark first next time.
  }
}
