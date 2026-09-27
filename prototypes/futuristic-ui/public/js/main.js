// Shell: sign-in, theme, navigation, global search and page lifecycle.
import {
  ApiError,
  getSession,
  onSession,
  signIn,
  signOut,
  startDemo,
} from './api.js';
import { createScope, startShared } from './feeds.js';
import { fleet } from './model.js';
import {
  resetFeeds,
  select,
  state,
  subscribe,
  syncHash,
  update,
  writePref,
} from './store.js';
import { dataState, esc, icon, patch, pulseIcon, wordmark } from './ui.js';
import { mountFleet } from './fleet.js';
import { mountDispatch } from './dispatch.js';

const app = document.getElementById('app');
let scope = null;
let shared = null;
let ctx = null;

function applyTheme() {
  document.documentElement.dataset.theme = state.theme;
}

function overallState() {
  const s = getSession();
  if (s?.mode === 'demo') return ['demo', ''];
  const f = state.feeds;
  const main = [f.locations, f.board];
  if (main.some((x) => x.state === 'unavailable'))
    return ['unavailable', 'API unreachable'];
  if (main.some((x) => x.state === 'stale'))
    return ['stale', `last good ${new Date(Math.min(...main.map((x) =>
      x.at || Date.now()))).toLocaleTimeString([], { hour: 'numeric',
      minute: '2-digit' })}`];
  if (main.some((x) => x.state === 'loading' || x.state === 'idle'))
    return ['loading', ''];
  return ['live', ''];
}

function signInScreen(error = '') {
  scope?.dispose();
  scope = null;
  app.innerHTML = `<div class="signin">
    <form class="panel" novalidate>
      ${wordmark()}
      <p class="eyebrow" style="text-align:center;margin:0 0 4px">
        Futuristic UI concept · local</p>
      <label>Email<input name="email" type="email" autocomplete="username"
        required></label>
      <label>Password<input name="password" type="password"
        autocomplete="current-password" required></label>
      <div class="err" role="alert">${esc(error)}</div>
      <button class="btn primary" type="submit">Sign in · live data</button>
      <p class="muted" style="font-size:var(--fs-small);margin:0">
        Uses the normal PulsR sign-in. The concept only reads: sending,
        payments and changes are refused by the local host.</p>
      <button class="btn" type="button" data-demo>
        Open labelled demo fixtures instead</button>
    </form></div>`;
  const form = app.querySelector('form');
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = new FormData(form);
    const btn = form.querySelector('[type=submit]');
    btn.disabled = true;
    btn.textContent = 'Signing in…';
    try {
      await signIn(String(fd.get('email')), String(fd.get('password')));
    } catch (err) {
      signInScreen(err instanceof ApiError ? err.message
        : 'The API could not be reached.');
    }
  });
  app.querySelector('[data-demo]').addEventListener('click', startDemo);
  form.querySelector('input').focus();
}

function shell() {
  const s = getSession();
  const initials = (s?.user?.name ?? '?').split(/\s+/).map((w) => w[0])
    .slice(0, 2).join('').toUpperCase();
  app.innerHTML = `<div class="app">
    <header class="topbar">
      <a class="brand" href="#/fleet" aria-label="PulsR TMS home">
        ${wordmark()}${pulseIcon()}</a>
      <nav class="crumbs eyebrow" aria-label="Context">
        <span>Operations</span><span>/</span>
        <b data-region="crumb"></b></nav>
      <span class="top-spacer"></span>
      <span data-region="state"></span>
      <button class="icon-btn" data-action="theme" data-key="theme"
        aria-label="Switch theme"></button>
      <div class="clock desktop-only" data-region="clock"></div>
      <div class="account" title="${esc(s?.user?.email ?? '')}">
        <span class="avatar">${esc(initials)}</span>
        <span>${esc(s?.user?.name ?? 'Signed in')}</span>
      </div>
      <button class="icon-btn" data-action="signout" aria-label="Sign out"
        title="Sign out">${icon('logout')}</button>
    </header>
    <nav class="rail" aria-label="Main">
      <a href="#/fleet" data-nav="fleet">${icon('map', 'lg')}Fleet Map</a>
      <a href="#/dispatch" data-nav="dispatch">${icon('board', 'lg')}Dispatch</a>
      <a aria-disabled="true" title="Not part of this concept">
        ${icon('chat', 'lg')}Messages</a>
      <a aria-disabled="true" title="Not part of this concept">
        ${icon('users', 'lg')}Customers</a>
      <div class="rail-foot">Read<br>only<br>concept</div>
    </nav>
    <main class="main" data-region="page"></main>
    <nav class="bottom-nav" aria-label="Main">
      <a href="#/fleet" data-nav="fleet">${icon('map')}Fleet</a>
      <a href="#/dispatch" data-nav="dispatch">${icon('board')}Dispatch</a>
      <button data-action="theme">${icon(state.theme === 'dark' ? 'sun'
        : 'moon')}Theme</button>
      <button data-action="signout">${icon('logout')}Sign out</button>
    </nav>
  </div>`;
  app.addEventListener('click', onShellClick);
}

function onShellClick(e) {
  const b = e.target.closest('[data-action]');
  if (!b) return;
  if (b.dataset.action === 'theme') {
    update((s) => (s.theme = s.theme === 'dark' ? 'light' : 'dark'));
    writePref('pulsr-concept-theme', state.theme);
    applyTheme();
  } else if (b.dataset.action === 'signout') signOut();
}

function renderChrome() {
  const [st, detail] = overallState();
  const r = (n) => app.querySelector(`[data-region="${n}"]`);
  if (!r('state')) return;
  patch(r('state'), dataState(st, detail));
  r('crumb').textContent = state.route === 'fleet' ? 'Fleet Map' : 'Dispatch';
  const tb = app.querySelector('.topbar [data-action="theme"]');
  tb.innerHTML = icon(state.theme === 'dark' ? 'sun' : 'moon');
  tb.setAttribute('aria-label', `Switch to ${state.theme === 'dark'
    ? 'light' : 'dark'} theme`);
  for (const a of app.querySelectorAll('[data-nav]'))
    if (a.dataset.nav === state.route) a.setAttribute('aria-current', 'page');
    else a.removeAttribute('aria-current');
}

function tick() {
  const c = app.querySelector('[data-region="clock"]');
  if (!c) return;
  const d = new Date();
  c.innerHTML = `${icon('clock')}<div><div class="d">${d.toLocaleDateString(
    'en-US', { weekday: 'short', month: 'short', day: 'numeric' })}</div>
    <div class="t">${d.toLocaleTimeString('en-US', { hour: 'numeric',
      minute: '2-digit' })}</div></div>`;
}

// Global search: trucks, drivers, trailers and loads from loaded data.
function searchBox(placeholder) {
  return `<div class="search" data-region="search">
    <label class="field">${icon('search')}
      <span class="sr-only">Search</span>
      <input type="search" placeholder="${esc(placeholder)}" data-key="q"
        value="${esc(state.query)}" autocomplete="off" role="combobox"
        aria-expanded="false" aria-controls="search-results">
      <span class="kbd desktop-only">/</span></label>
    <ul class="results" id="search-results" role="listbox" hidden></ul>
  </div>`;
}

function searchResults(q) {
  if (!q) return [];
  const needle = q.toLowerCase();
  const out = [];
  for (const t of fleet().trucks) {
    if ([t.unit, t.driver, t.trailer].some((v) =>
      String(v ?? '').toLowerCase().includes(needle)))
      out.push({ kind: 'Truck', label: `${t.unit}`, sub: `${t.driver} · ${
        t.motion}`, truckId: t.id });
    for (const x of t.trips)
      if (x.loadNumber.toLowerCase().includes(needle))
        out.push({ kind: 'Load', label: x.loadNumber, sub: `Truck ${t.unit} · ${
          x.fromText} → ${x.toText}`, truckId: t.id, dispatchId: x.id });
  }
  return out.slice(0, 12);
}

function wireSearch(root) {
  const box = root.querySelector('[data-region="search"]');
  if (!box) return;
  const input = box.querySelector('input');
  const list = box.querySelector('.results');
  let items = [];
  let active = -1;
  const draw = () => {
    list.hidden = !items.length;
    input.setAttribute('aria-expanded', String(!list.hidden));
    list.innerHTML = items.map((it, i) => `<li role="option" id="sr-${i}"
      aria-selected="${i === active}" data-i="${i}"><span class="kind">${
      it.kind}</span><span><b>${esc(it.label)}</b><br><span class="muted">${
      esc(it.sub)}</span></span></li>`).join('');
    if (active >= 0) input.setAttribute('aria-activedescendant',
      `sr-${active}`);
    else input.removeAttribute('aria-activedescendant');
  };
  const pick = (it) => {
    items = [];
    draw();
    ctx.onSearchPick?.(it);
  };
  input.addEventListener('input', () => {
    update((s) => (s.query = input.value));
    items = searchResults(input.value);
    active = items.length ? 0 : -1;
    draw();
  });
  input.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowDown' && items.length) {
      e.preventDefault();
      active = (active + 1) % items.length;
      draw();
    } else if (e.key === 'ArrowUp' && items.length) {
      e.preventDefault();
      active = (active - 1 + items.length) % items.length;
      draw();
    } else if (e.key === 'Enter' && items[active]) {
      e.preventDefault();
      pick(items[active]);
    } else if (e.key === 'Escape') {
      items = [];
      draw();
    }
  });
  list.addEventListener('pointerdown', (e) => {
    const li = e.target.closest('li');
    if (li) {
      e.preventDefault();
      pick(items[Number(li.dataset.i)]);
    }
  });
  input.addEventListener('blur', () => setTimeout(() => {
    items = [];
    draw();
  }, 120));
}

function mountPage() {
  scope?.dispose();
  scope = createScope();
  shared = startShared(scope);
  const page = app.querySelector('[data-region="page"]');
  ctx = { searchBox, onState: null, onSearchPick: null, shared };
  if (state.route === 'dispatch') mountDispatch(page, scope, ctx);
  else mountFleet(page, scope, ctx);
  wireSearch(page);
  renderChrome();
}

function readHash() {
  const [path, query] = location.hash.replace(/^#\/?/, '').split('?');
  const p = new URLSearchParams(query ?? '');
  return {
    route: path === 'dispatch' ? 'dispatch' : 'fleet',
    truckId: p.get('truck'),
    dispatchId: p.get('load'),
  };
}

function start() {
  applyTheme();
  const s = getSession();
  if (!s) return signInScreen();
  shell();
  const h = readHash();
  state.route = h.route;
  if (h.truckId) select({ truckId: h.truckId, dispatchId: h.dispatchId });
  mountPage();
  syncHash();
  tick();
}

subscribe(() => {
  renderChrome();
  ctx?.onState?.();
});

window.addEventListener('hashchange', () => {
  if (!getSession()) return;
  const h = readHash();
  if (h.route !== state.route) {
    state.route = h.route;
    mountPage();
  }
  if (h.truckId && h.truckId !== state.selection.truckId)
    select({ truckId: h.truckId, dispatchId: h.dispatchId });
  syncHash();
});

document.addEventListener('keydown', (e) => {
  if (e.key === '/' && !/input|textarea/i.test(e.target.tagName)) {
    const input = app.querySelector('[data-region="search"] input');
    if (input) {
      e.preventDefault();
      input.focus();
    }
  }
});

let lastMode = getSession()?.mode;
onSession((s) => {
  if (s?.mode !== lastMode || !s) {
    lastMode = s?.mode;
    resetFeeds();
    select({ truckId: null });
    app.removeEventListener('click', onShellClick);
    start();
  }
});

// ?fixtures=1 opens the labelled demo directly (screenshots, layout tests);
// ?theme=light|dark forces a theme. Neither affects a live session.
const params = new URLSearchParams(location.search);
if (params.get('theme') === 'dark' || params.get('theme') === 'light')
  state.theme = params.get('theme');
if (params.get('fixtures') === '1' && getSession()?.mode !== 'live') {
  lastMode = 'demo';
  startDemo();
}
setInterval(tick, 15000);
start();
tick();
