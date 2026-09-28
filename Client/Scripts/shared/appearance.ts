// The theme on the page, and remembered in this browser so the next visit
// paints in it before the app has loaded (index.html reads it; dark when
// nothing was ever applied here). The account's saved choice still decides
// once it is read.
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
