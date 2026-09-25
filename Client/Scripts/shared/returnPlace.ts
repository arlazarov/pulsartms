// Where the reader was on a page they left to open a load, so that coming
// back puts them there again. Held in memory for this tab only: a reload or
// a new tab starts at the top, as a fresh page does.
const positions = new Map<string, number>();
let armed: string | null = null;

const here = () => location.pathname + location.search;

const record = () => {
  if (armed !== null && armed === here()) positions.set(armed, window.scrollY);
};

addEventListener('scroll', record, { passive: true });

// The page's state, written into its own history entry so browser Back
// returns to it. The router's state object is kept, which its navigation
// guard needs; nothing is rendered again. Only the page's own entry is
// rewritten: a read that finishes after the reader moved on must not
// retitle the page they are on now.
export function reflect(url: string) {
  const target = new URL(url, location.href);
  if (target.origin !== location.origin) return;
  if (target.pathname !== location.pathname) return;
  if (here() === url) return;
  const wasArmed = armed === here();
  history.replaceState(history.state, '', url);
  if (wasArmed) armed = here();
}

// Called once the page has drawn what it was showing: the position kept for
// this address, if any, is restored, and later scrolling is kept for it.
// Until then the page is short while it loads, and the browser's clamped
// scroll would overwrite what was kept.
export function arm() {
  const position = positions.get(here());
  if (position !== undefined) window.scrollTo(0, position);
  armed = here();
}

export function disarm() {
  armed = null;
}

// Where a conversation was being read: the message at the top of the view
// and how far down the view it stood. Taken when the conversation opens
// again; nothing is kept while the newest message is in view.
export type ThreadAnchor = { id: string; top: number };
const threads = new Map<string, ThreadAnchor>();

export function keepThread(key: string, anchor: ThreadAnchor | null) {
  if (anchor) threads.set(key, anchor);
  else threads.delete(key);
}

export function takeThread(key: string) {
  const anchor = threads.get(key) ?? null;
  threads.delete(key);
  return anchor;
}

// Another user or another company: nothing kept for the last one is shown.
export function forget() {
  positions.clear();
  threads.clear();
  armed = null;
}
