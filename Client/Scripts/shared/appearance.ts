// The theme on the page, and remembered in this browser so the next visit
// paints in it before the app has loaded (index.html reads it; dark when
// nothing was ever applied here). The account's saved choice still decides
// once it is read.
export function applyTheme(theme: string): void {
  const value = theme === 'dark' ? 'dark' : 'light';
  document.documentElement.dataset.theme = value;
  try {
    localStorage.setItem('pulsr.theme', value);
  } catch {
    // A browser that refuses storage paints dark first next time.
  }
}
