import test from 'node:test';
import assert from 'node:assert/strict';

// A small stand-in for the browser: one history entry whose address the
// module may rewrite, and a window it scrolls.
const page = { url: new URL('https://app.test/dispatch'), y: 0, scroll: null };
const routerState = { userState: null, _index: 3 };
globalThis.location = {
  get href() {
    return page.url.href;
  },
  get origin() {
    return page.url.origin;
  },
  get pathname() {
    return page.url.pathname;
  },
  get search() {
    return page.url.search;
  },
};
globalThis.history = {
  state: routerState,
  replaceState(state, _title, url) {
    assert.equal(state, routerState);
    page.url = new URL(url, page.url);
  },
};
globalThis.window = {
  get scrollY() {
    return page.y;
  },
  scrollTo(_x, y) {
    page.y = y;
  },
};
globalThis.addEventListener = (name, listener) => {
  if (name === 'scroll') page.scroll = listener;
};

const place = await import('../../Scripts/shared/returnPlace.ts');

const go = url => {
  page.url = new URL(url, page.url);
};
const scroll = y => {
  page.y = y;
  page.scroll();
};

test("only the page's own address changes; router state kept", () => {
  go('/dispatch');
  place.reflect('/dispatch?scope=completed&q=11006');
  assert.equal(
    page.url.pathname + page.url.search,
    '/dispatch?scope=completed&q=11006',
  );

  // The reader has already moved to a load: a late write must not
  // retitle it, nor may any address leave this app.
  go('/dispatch/9b0c9c1e-6a47-4c58-9d0a-0d4c5f3e2a11');
  place.reflect('/dispatch?page=2');
  place.reflect('https://example.com/dispatch');
  assert.equal(
    page.url.pathname,
    '/dispatch/9b0c9c1e-6a47-4c58-9d0a-0d4c5f3e2a11',
  );
});

test('scroll is kept once the page is drawn, and restored', () => {
  place.forget();
  go('/dispatch?page=2');
  place.arm();
  scroll(640);
  place.disarm();

  // Leaving: the new page's clamped scroll is not the list's.
  go('/dispatch/9b0c9c1e-6a47-4c58-9d0a-0d4c5f3e2a11');
  scroll(0);

  // Back to the list: short while it loads, then drawn and restored.
  go('/dispatch?page=2');
  scroll(0);
  assert.equal(page.y, 0);
  place.arm();
  assert.equal(page.y, 640);
});

test('another user finds nothing kept', () => {
  go('/dispatch?q=kept');
  place.arm();
  scroll(300);
  place.disarm();
  place.forget();
  page.y = 0;
  place.arm();
  assert.equal(page.y, 0);
});
