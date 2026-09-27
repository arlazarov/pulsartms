// Dispatch board: attention and assignment queue (left), truck lanes with
// their trips in order (centre), selected trip details (right) and the same
// trip chain as Fleet (bottom). Selection is shared with Fleet.
import { fleet, selectedTrip, selectedTruck, matches } from './model.js';
import { select, state, update } from './store.js';
import { attentionList, chain, hos, stopList } from './shared-views.js';
import { dataState, esc, icon, patch, phaseBadge } from './ui.js';
import { getSession } from './api.js';
import {
  appointment,
  formatClock,
  hosClocks,
  isLowClock,
  location,
  phaseLabel,
  text,
} from './rules.js';

const PAGE = 12;
const isPhone = () => matchMedia('(max-width: 767px)').matches;

export function mountDispatch(root, scope, ctx) {
  root.innerHTML = `
    <header class="page-head">
      <div class="page-title desktop-only">
        <h1>Dispatch</h1>
        <p class="eyebrow">Assignments · current and next work</p>
      </div>
      ${ctx.searchBox('Truck, driver, trailer or load')}
      <div class="chips dispatch-chips" data-region="chips"></div>
    </header>
    <div class="workspace dispatch" data-region="ws">
      <section class="panel left" aria-label="Queue" data-region="left">
      </section>
      <section class="panel center board" aria-label="Dispatch board"
        data-region="board"></section>
      <section class="panel right" aria-label="Trip details"
        data-region="right"></section>
      <section class="panel dock" aria-label="Trip chain" data-region="dock">
      </section>
    </div>`;
  const r = (n) => root.querySelector(`[data-region="${n}"]`);
  const ws = r('ws');

  ctx.onSearchPick = (item) => {
    select({ truckId: item.truckId, dispatchId: item.dispatchId ?? null });
    reveal(item.truckId);
  };

  function reveal(truckId) {
    const list = visible();
    const i = list.findIndex((t) => t.id === truckId);
    if (i >= 0) update((s) => (s.dispatchPage = Math.floor(i / PAGE) + 1));
    requestAnimationFrame(() =>
      root.querySelector(`[data-lane="${truckId}"]`)?.scrollIntoView({
        block: 'nearest',
        behavior: 'smooth',
      })
    );
  }

  function visible() {
    const { trucks } = fleet();
    return trucks.filter((t) => {
      if (!matches(t, state.query)) return false;
      if (state.dispatchFilter === 'attention') return t.attention.length > 0;
      if (state.dispatchFilter === 'open') return t.trips.length <= 1;
      return true;
    });
  }

  function render() {
    const model = fleet();
    const t = selectedTruck();
    const trip = selectedTrip(t) ?? unassignedSelected(model);
    const phone = isPhone();
    ws.dataset.left = phone ? (state.mobileSheet === 'list' ? 'open'
      : 'closed') : state.leftOpen ? 'open' : 'closed';
    ws.dataset.right = phone
      ? (state.mobileSheet === 'detail' && trip ? 'open' : 'closed')
      : state.rightOpen ? 'open' : 'closed';
    patch(r('chips'), chips(model));
    patch(r('left'), queue(model));
    patch(r('board'), board(model, t));
    patch(r('right'), detail(t, trip));
    patch(r('dock'), chain(t, trip?.id));
  }

  root.addEventListener('click', (e) => {
    const b = e.target.closest('[data-action]');
    if (!b) return;
    const a = b.dataset.action;
    if (a === 'lane') {
      select({ truckId: b.dataset.id });
    } else if (a === 'work' || a === 'trip') {
      const truckId = b.dataset.truck ?? state.selection.truckId;
      select({ truckId, dispatchId: b.dataset.id });
      if (isPhone()) update((s) => (s.mobileSheet = 'detail'));
    } else if (a === 'unassigned') {
      select({ truckId: null });
      select({ dispatchId: b.dataset.id });
      if (isPhone()) update((s) => (s.mobileSheet = 'detail'));
    } else if (a === 'attention') {
      select({ truckId: b.dataset.truck });
      reveal(b.dataset.truck);
    } else if (a === 'stop') {
      select({ dispatchId: b.dataset.trip, stopId: b.dataset.id });
    } else if (a === 'dfilter') {
      update((s) => {
        s.dispatchFilter = b.dataset.value;
        s.dispatchPage = 1;
      });
    } else if (a === 'page') {
      update((s) => (s.dispatchPage += Number(b.dataset.step)));
      r('board').querySelector('[data-scroll="lanes"]')?.scrollTo(0, 0);
    } else if (a === 'toggle-left') {
      update((s) => (s.leftOpen = !s.leftOpen));
    } else if (a === 'sheet') {
      update((s) => (s.mobileSheet = s.mobileSheet === b.dataset.value
        ? 'none' : b.dataset.value));
    } else if (a === 'close-detail') {
      update((s) => {
        if (isPhone()) s.mobileSheet = 'none';
        else s.rightOpen = false;
      });
    } else if (a === 'open-detail') {
      update((s) => (s.rightOpen = true));
    }
  });

  ctx.onState = render;
  render();
  if (state.selection.truckId) reveal(state.selection.truckId);

  function chips(model) {
    const trucks = model.trucks;
    const n = {
      all: trucks.length,
      attention: trucks.filter((t) => t.attention.length).length,
      open: trucks.filter((t) => t.trips.length <= 1).length,
    };
    const c = (v, label, dot) => `<button class="chip" data-action="dfilter"
      data-value="${v}" data-key="dchip-${v}"
      aria-pressed="${state.dispatchFilter === v}">${dot
        ? `<span class="dot ${dot}"></span>` : ''}${label}
      <span class="count">${n[v]}</span></button>`;
    return c('all', 'All trucks') + c('attention', 'Attention', 'attention') +
      c('open', 'No next load', 'offline') +
      `<button class="chip mobile-only" data-action="sheet" data-value="list"
        aria-pressed="${state.mobileSheet === 'list'}">${icon('list', 'sm')}
        Queue<span class="count">${model.unassigned.length + n.attention}
        </span></button>`;
  }

  function queue(model) {
    const att = model.trucks.flatMap((t) =>
      t.attention.map((a) => ({ ...a, t }))
    );
    const f = state.feeds.board;
    return `<span class="sheet-handle mobile-only"></span>
      <div class="panel-head"><h2>Queue</h2>
        <button class="icon-btn mobile-only" data-action="sheet"
          data-value="list" aria-label="Close queue">${icon('close')}</button>
      </div>
      <div class="panel-body" data-scroll="queue">
        <div class="queue-group eyebrow">Awaiting assignment · ${
          model.unassigned.length}</div>
        ${model.unassigned.map((d) => {
          const from = d.stops?.[0];
          const to = d.stops?.at(-1);
          return `<button class="queue-item" data-action="unassigned"
            data-id="${d.id}" data-key="u-${d.id}"
            aria-pressed="${state.selection.dispatchId === d.id}">
            <span class="bar info"></span>
            <span class="t">${esc(text(d.loadNumber))}</span>
            <span class="muted">${esc(appointment(from, d.shipDate))}</span>
            <span class="s">${esc(location(from))} → ${esc(location(to))}
            </span></button>`;
        }).join('') || '<p class="note">No loads are waiting for a truck.</p>'}
        <div class="queue-group eyebrow">Attention · ${att.length}</div>
        ${att.map((a, i) => `<button class="queue-item" data-action="attention"
            data-truck="${a.t.id}" data-key="a-${i}">
            <span class="bar ${a.level === 'danger' ? 'danger' : ''}"></span>
            <span class="t">Truck ${esc(a.t.unit)}</span>
            <span class="muted">${esc(a.t.driver)}</span>
            <span class="s">${esc(a.text)}${a.detail ? `<br><span
              class="muted">${esc(a.detail)}</span>` : ''}</span></button>`)
          .join('') ||
          '<p class="note">Nothing needs attention.</p>'}
        <p class="note">Assigning and reassigning stay in the production
          Dispatch workspace; this concept only reads.</p>
      </div>
      <div class="panel-foot"><span>Board projection</span>${dataState(
        getSession()?.mode === 'demo' ? 'demo' : f.state)}</div>`;
  }

  function board(model, t) {
    const list = visible();
    const pages = Math.max(1, Math.ceil(list.length / PAGE));
    const page = Math.min(state.dispatchPage, pages);
    const slice = list.slice((page - 1) * PAGE, page * PAGE);
    const lanes = slice.map((x) => lane(x, x.id === t?.id)).join('');
    return `<div class="board-head">
        <button class="icon-btn desktop-only" data-action="toggle-left"
          aria-label="Show or hide queue" data-key="dq">${icon('panelLeft')}
        </button>
        <b>${list.length} trucks</b>
        <span class="muted">· Ordered by truck number · phases as the
          board shows them</span>
        <span class="top-spacer"></span>
        ${state.rightOpen || isPhone() ? '' : `<button class="btn"
          data-action="open-detail">${icon('panelRight', 'sm')}Details
          </button>`}
      </div>
      <div class="panel-body" data-scroll="lanes">
        <ol class="lanes">${lanes || `<li class="empty">${
          state.feeds.board.state === 'unavailable'
            ? `<h3>Board unavailable</h3>${esc(state.feeds.board.error ?? '')}`
            : state.feeds.board.data ? 'No trucks match.'
            : 'Loading the board…'}</li>`}</ol>
      </div>
      <div class="pager">
        <button class="btn" data-action="page" data-step="-1"
          ${page <= 1 ? 'disabled' : ''}>Previous</button>
        <span>Page ${page} of ${pages}</span>
        <button class="btn" data-action="page" data-step="1"
          ${page >= pages ? 'disabled' : ''}>Next</button>
      </div>`;
  }

  function lane(t, selected) {
    const selTrip = state.selection.dispatchId;
    const work = t.trips.map((x, i) => {
      const flags = t.attention.filter((a) =>
        a.stopId && x.stops.some((s) => s.id === a.stopId));
      return `${i ? `<span class="work-arrow">${icon('right', 'sm')}</span>`
        : ''}<button class="work ${x.phase}" data-action="work"
        data-id="${x.id}" data-truck="${t.id}" data-key="w-${x.id}"
        aria-pressed="${selected && (selTrip ? selTrip === x.id
          : x.phase === 'current')}">
        <span class="top"><span class="load">${esc(x.loadNumber)}</span>
          ${phaseBadge(x.phase, phaseLabel[x.phase])}</span>
        <span class="lane">${esc(x.fromText)} → ${esc(x.toText)}</span>
        <span class="meta"><span>${esc(x.status)}</span><span>${esc(
          appointment(x.to))}</span></span>
        ${flags.map((f) => `<span class="meta flag">${icon('alert', 'sm')}
          ${esc(f.text)}</span>`).join('')}
      </button>`;
    }).join('');
    const tail = t.trips.length <= 1
      ? `<span class="work-arrow">${icon('right', 'sm')}</span>
        <div class="work empty-slot"><b>No next load</b>
        <span>Nothing scheduled after this${t.trips.length ? ' load' : ''}
        </span></div>` : '';
    return `<li class="lane-row" data-lane="${t.id}"
      data-selected="${selected}">
      <button class="lane-truck" data-action="lane" data-id="${t.id}"
        data-key="lane-${t.id}" aria-pressed="${selected}">
        <span class="unit"><span class="dot ${t.moving ? 'moving'
          : 'stopped'}"></span>${esc(t.unit)}${t.attention.length
          ? `<span style="color:var(--warn)">${icon('alert', 'sm')}</span>`
          : ''}</span>
        <span class="who">${esc(t.driver)} · Trailer ${esc(text(t.trailer))}
        </span>
        <span class="status-pill">${esc(t.motion)}</span>
        <span class="mini-hos">${hosClocks(t.hos).map(([k, ms]) =>
          `<span class="${isLowClock(ms) ? 'low' : ''}">${k[0]} <b>${
            formatClock(ms)}</b></span>`).join('')}</span>
      </button>
      <div class="lane-trips" data-scroll="lane-${t.id}">${work}${tail}</div>
    </li>`;
  }

  function detail(t, trip) {
    if (!trip)
      return `<div class="empty" style="margin:auto"><h3>No trip selected
        </h3>Choose a trip on the board.</div>`;
    const load = trip.load ?? trip;
    const unassigned = !t;
    const money = load.price != null
      ? `${Number(load.price).toLocaleString('en-US', { style: 'currency',
        currency: load.currency || 'USD', maximumFractionDigits: 0 })}`
      : '—';
    return `<span class="sheet-handle mobile-only"></span>
      <div class="truck-head">
        <div style="flex:1;min-width:0">
          <span class="eyebrow">${unassigned ? 'Awaiting assignment'
            : `Truck ${esc(t.unit)} · trip ${trip.index}`}</span>
          <h2>${esc(text(load.loadNumber))}</h2>
          <div class="sub">${unassigned ? '' : phaseBadge(trip.phase,
            phaseLabel[trip.phase])} ${esc(trip.status ?? 'Unassigned')}</div>
        </div>
        <button class="icon-btn" data-action="close-detail"
          aria-label="Close details" data-key="close-d">${icon('close')}
        </button>
      </div>
      <div class="panel-body" data-scroll="ddetail">
        <dl class="facts">
          ${fact('box', 'Customer', load.customerName)}
          ${fact('list', 'Order', load.orderNumber)}
          ${fact('route', 'Loaded miles', load.loadedMiles != null
            ? `${load.loadedMiles} mi` : '—')}
          ${fact('fuel', 'Rate', money)}
          ${unassigned ? '' : fact('user', 'Driver', t.driver)}
          ${unassigned ? '' : fact('trailer', 'Trailer', text(t.trailer))}
        </dl>
        ${unassigned ? '' : attentionList(t)}
        ${unassigned ? '' : hos(t)}
        <div class="section-title"><h3>Stops · ${(trip.stops ??
          load.stops ?? []).length}</h3></div>
        <div style="padding:0 var(--sp-lg)">
          ${stopList(t, trip.stops ? trip : { id: load.id,
            stops: load.stops ?? [] })}</div>
        <div style="display:flex;gap:8px;padding:var(--sp-md) var(--sp-lg)">
          ${unassigned ? `<button class="btn primary block" disabled
            title="Read-only concept">Assign truck</button>`
            : `<a class="btn primary block" href="#/fleet?truck=${t.id}&load=${
              trip.id}">${icon('map', 'sm')}Locate on map</a>`}
          <button class="btn block" disabled title="Read-only concept">
            Open load</button>
        </div>
        <p class="note">Editing, assigning and messaging are disabled: the
          concept reads the same projections the Dispatch page uses.</p>
      </div>`;
  }
}

function unassignedSelected(model) {
  const id = state.selection.dispatchId;
  const d = id && model.unassigned.find((x) => x.id === id);
  if (!d) return null;
  return {
    id: d.id,
    load: d,
    stops: [...(d.stops ?? [])].sort((a, b) => a.sequence - b.sequence),
    status: 'Unassigned',
  };
}

function fact(ic, k, v) {
  return `<div class="fact">${icon(ic)}<div><dt>${esc(k)}</dt>
    <dd title="${esc(v ?? '—')}">${esc(v ?? '—')}</dd></div></div>`;
}
