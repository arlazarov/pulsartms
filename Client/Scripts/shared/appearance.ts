// The theme on the page, and remembered in this browser so the next visit
// paints in it before the app has loaded (index.html reads it; dark when
// nothing was ever applied here). The account's saved choice still decides
// once it is read.
// A switch moves the page's colours to the new theme together, briefly
// (the owner, September 28): about a quarter second on a desktop, less on
// a phone, nothing with reduced motion. Only colours move - nothing is
// captured, blurred or laid out again - and a quick second switch just
// restarts the one short window. The map is made again in the new scheme
// by its own owner and fades in over the old one.
let settle: ReturnType<typeof setTimeout> | undefined;

export function applyTheme(theme: string): void {
  const value = theme === 'dark' ? 'dark' : 'light';
  const root = document.documentElement;
  const changing = !!root.dataset.theme && root.dataset.theme !== value;
  const media = (query: string) => globalThis.matchMedia?.(query).matches;
  if (changing && !media('(prefers-reduced-motion: reduce)')) {
    const lite = media('(max-width: 767px), (pointer: coarse)');
    root.style.setProperty('--theme-fade', lite ? '150ms' : '250ms');
    root.classList.add('is-theme-switching');
    clearTimeout(settle);
    settle = setTimeout(
      () => root.classList.remove('is-theme-switching'),
      lite ? 200 : 300,
    );
  }
  root.dataset.theme = value;
  try {
    localStorage.setItem('pulsr.theme', value);
  } catch {
    // A browser that refuses storage paints dark first next time.
  }
}
