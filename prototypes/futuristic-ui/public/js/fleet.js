// Fleet Map screen: fleet list (left), map (centre), selected truck with
// Route/Fuel tabs (right) and the trip chain (bottom).
import { FleetMapView } from './map.js';
import { decodePolyline } from './geo.js';
import { startSelected } from './feeds.js';
import {
  filterTrucks,
  fleet,
  selectedTrip,
  selectedTruck,
} from './model.js';
import { select, state, update, writePref } from './store.js';
import {
  attentionList,
  chain,
  hos,
  tripCard,
} from './shared-views.js';
import { dataState, esc, icon, patch } from './ui.js';
import { getSession } from './api.js';

const ctxMode = () => getSession()?.mode;
import {
  appointment,
  dutyLabel,
  formatInstant,
  location,
  stopJob,
  text,
  tripStopLabels,
} from './rules.js';

export function mountFleet(root, scope, ctx) {
  root.innerHTML = `
    <header class="page-head">
      <div class="page-title desktop-only">
        <h1>Fleet Map</h1>
        <p class="eyebrow">Live positions · trips · fuel</p>
      </div>
      ${ctx.searchBox('Search truck, driver, trailer or load')}
      <div class="chips" data-region="chips"></div>
    </header>
    <div class="workspace" data-region="ws">
      <section class="panel left" aria-label="Fleet list" data-region="left">
      </section>
      <section class="panel center map-wrap" aria-label="Map" data-region="map">
        <div class="map-ctl tl">
          <button class="map-btn desktop-only" data-action="toggle-left"
            data-key="tl-left" aria-label="Show or hide fleet list">
            ${icon('panelLeft')}</button>
          <div class="mobile-only tabs" role="tablist" data-region="msheet"
            style="background:var(--panel)"></div>
        </div>
        <div class="map-ctl tr" role="toolbar" aria-label="Map tools">
          <button class="map-btn" data-action="follow" data-key="map-follow"
            aria-label="Follow" title="Follow" aria-pressed="false">
            ${icon('compass')}</button>
          <button class="map-btn" data-action="fit-route" data-key="fit-route"
            aria-label="Fit the selected route" title="Fit route">
            ${icon('fit')}</button>
          <button class="map-btn" data-action="tab" data-value="fuel"
            data-key="map-fuel" aria-label="Fuel" title="Fuel">
            ${icon('fuel')}</button>
          <div style="position:relative">
            <button class="map-btn" data-action="layers" data-key="layers"
              aria-label="Map layers" title="Map layers"
              aria-expanded="false">${icon('layers')}</button>
            <div class="layer-menu" data-region="layers" hidden></div>
          </div>
          <button class="map-btn desktop-only" data-action="toggle-right"
            data-key="tl-right" aria-label="Show or hide truck details">
            ${icon('panelRight')}</button>
        </div>
        <div class="map-ctl bl" data-region="mapnote"></div>
      </section>
      <section class="panel right" aria-label="Selected truck"
        data-region="right"></section>
      <section class="panel dock" aria-label="Trip chain" data-region="dock">
      </section>
    </div>`;

  const r = (name) => root.querySelector(`[data-region="${name}"]`);
  const ws = r('ws');
  const sel = startSelected(scope);
  let lastTruck = state.selection.truckId;
  let fittedFor = null;
  let basemapFailed = false;
  let userMoved = false;

  const map = new FleetMapView(r('map'), {
    onTruck: (id) => chooseTruck(id),
    onStop: (tripId, stopId) => chooseStop(tripId, stopId),
    onTrip: (id) => chooseTrip(id),
    onPan: () => (userMoved = true),
    onFollowChange: (on) => update((s) => (s.following = on)),
    onBackground: () => {
      if (state.selection.stopId) select({ stopId: null });
      update((s) => s.mobileSheet === 'list' && (s.mobileSheet = 'none'));
    },
    onBasemapError: () => {
      basemapFailed = true;
      render();
    },
  });
  scope.add(() => map.destroy());

  function chooseTruck(id) {
    if (id !== state.selection.truckId) map.endFollow();
    userMoved = false;
    select({ truckId: id });
    update((s) => {
      s.rightOpen = true;
      if (s.mobileSheet !== 'none') s.mobileSheet = 'detail';
      else if (matchMedia('(max-width: 767px)').matches)
        s.mobileSheet = 'peek';
    });
  }
  function chooseTrip(id) {
    select({ dispatchId: id });
    fitTrip(id);
  }
  function chooseStop(tripId, stopId) {
    select({ dispatchId: tripId, stopId });
    const s = selectedTrip()?.stops.find((x) => x.id === stopId);
    if (s?.latitude) map.fit([[s.latitude, s.longitude]]);
  }

  ctx.onSearchPick = (item) => {
    if (item.truckId) chooseTruck(item.truckId);
    if (item.dispatchId) chooseTrip(item.dispatchId);
  };

  function routesFor(t) {
    const out = [];
    if (!t) return out;
    const plan = state.feeds.planning.data;
    const focus = state.selection.dispatchId;
    if (plan?.truckId === t.id && plan.state?.plan?.route) {
      const pts = plan.state.plan.route.legs
        ?.flatMap((l) => (l.path ? decodePolyline(l.path, 6)
          : (l.points ?? []).map((p) => [p.latitude, p.longitude]))) ?? [];
      const x = t.trips.find((y) => y.id === plan.dispatchId);
      out.push({
        id: plan.dispatchId,
        phase: x?.phase ?? 'current',
        points: pts,
        title: `${plan.loadNumber ?? ''} current route`,
      });
    }
    const next = state.feeds.next.data;
    if (next?.truckId === t.id && state.layers.future) {
      for (const rt of next.routes ?? []) {
        const x = t.trips.find(
          (y) => y.id === rt.id || y.loadNumber === rt.loadNumber
        );
        const pts = (rt.legs ?? []).flatMap((l) =>
          l.path ? decodePolyline(l.path, 6)
            : (l.points ?? []).map((p) => [p.latitude, p.longitude]));
        if (rt.deadhead?.path)
          out.push({
            id: `${rt.id}-dh`,
            phase: 'later',
            points: decodePolyline(rt.deadhead.path, 6),
          });
        out.push({
          id: x?.id ?? rt.id,
          phase: x?.phase === 'current' ? 'next' : x?.phase ?? 'upcoming',
          points: pts,
          title: `${rt.loadNumber} future route`,
        });
      }
    }
    for (const o of out) {
      o.focus = focus && o.id === focus;
      o.dim = focus && o.id !== focus;
    }
    return out;
  }

  function stopsFor(t) {
    if (!t) return [];
    const focus = state.selection.dispatchId;
    const out = [];
    const trips = state.layers.future ? t.trips
      : t.trips.filter((x) => x.phase === 'current');
    for (const x of trips) {
      const labels = tripStopLabels(x.stops.map((s) => s.job));
      x.stops.forEach((s, i) => {
        if (s.latitude == null) return;
        const last = i === x.stops.length - 1;
        out.push({
          id: s.id,
          tripId: x.id,
          lat: s.latitude,
          lng: s.longitude,
          phase: x.phase === 'completed' ? 'later' : x.phase,
          label: labels[i],
          major: true,
          dim: focus && focus !== x.id,
          selected: state.selection.stopId === s.id,
          aria: `${x.loadNumber} ${labels[i]} ${stopJob(s.job)} ${location(
            s)}`,
        });
      });
    }
    return out;
  }

  function render() {
    const { trucks } = fleet();
    const t = selectedTruck();
    const trip = selectedTrip(t);
    ws.dataset.left = state.leftOpen || state.mobileSheet === 'list'
      ? 'open' : 'closed';
    ws.dataset.right = t && (state.mobileSheet === 'none'
      ? state.rightOpen && !isPhone() : ['detail', 'peek'].includes(
        state.mobileSheet)) ? 'open' : isPhone() ? 'closed'
      : state.rightOpen ? 'open' : 'closed';
    ws.dataset.sheet = state.mobileSheet;
    if (isPhone()) ws.dataset.left = state.mobileSheet === 'list'
      ? 'open' : 'closed';

    patch(r('chips'), chips(trucks));
    patch(r('left'), leftPanel(trucks, t));
    patch(r('right'), rightPanel(t, trip));
    patch(r('dock'), chain(t, trip?.id));
    patch(r('msheet'), mobileTabs(t));
    patch(r('mapnote'), mapNote());
    root.querySelector('[data-key="map-follow"]')
      ?.setAttribute('aria-pressed', state.following ? 'true' : 'false');
    const layers = r('layers');
    if (!layers.hidden) patch(layers, layerMenu());

    const routes = routesFor(t);
    map.setData({
      trucks: filterTrucks(trucks, state.filter, state.query),
      selectedId: t?.id ?? null,
      routes,
      routeKey: JSON.stringify([t?.id, state.feeds.planning.at,
        state.feeds.next.at, state.selection.dispatchId, state.layers.future]),
      stops: stopsFor(t),
      layers: state.layers,
    });
    if (!fittedFor && trucks.length) {
      fittedFor = 'fleet';
      if (!t) map.fit(trucks.map((x) => [x.lat, x.lng]).filter((p) =>
        p[0] != null), true);
    }
    // Refit while the selected truck's chain is still arriving, until the
    // user moves the map themselves.
    const fitKey = t ? `${t.id}:${t.trips.length}:${routes.length}` : null;
    if (t && fittedFor !== fitKey && !userMoved && !state.following) {
      fittedFor = fitKey;
      fitTruck(t, routes);
    }
  }

  function fitTruck(t, routes = routesFor(t)) {
    // The whole chain: every trip's stops, its drawn routes and the truck.
    const pts = routes.flatMap((x) => x.points);
    for (const x of state.layers.future ? t.trips : t.trips.slice(0, 1))
      for (const s of x.stops)
        if (s.latitude != null) pts.push([s.latitude, s.longitude]);
    if (t.lat != null) pts.push([t.lat, t.lng]);
    map.fit(pts.length ? pts : [[t.lat, t.lng]]);
  }
  function fitTrip(id) {
    const t = selectedTruck();
    const route = routesFor(t).find((x) => x.id === id);
    const trip = t?.trips.find((x) => x.id === id);
    const pts = route?.points?.length ? route.points
      : trip?.stops.filter((s) => s.latitude != null)
        .map((s) => [s.latitude, s.longitude]) ?? [];
    if (pts.length) map.fit(pts);
  }

  root.addEventListener('click', (e) => {
    const b = e.target.closest('[data-action]');
    if (!b) return;
    const a = b.dataset.action;
    const id = b.dataset.id;
    if (a === 'truck') chooseTruck(id);
    else if (a === 'trip') chooseTrip(id);
    else if (a === 'stop') chooseStop(b.dataset.trip, id);
    else if (a === 'expand')
      update((s) => {
        const open = isOpen(id);
        if (open) {
          s.expanded.delete(id);
          s.collapsed.add(id);
        } else {
          s.collapsed.delete(id);
          s.expanded.add(id);
          select({ dispatchId: id });
        }
      });
    else if (a === 'filter') update((s) => (s.filter = b.dataset.value));
    else if (a === 'tab') update((s) => (s.tab = b.dataset.value));
    else if (a === 'toggle-left')
      update((s) => {
        s.leftOpen = !s.leftOpen;
        writePref('pulsr-concept-left', s.leftOpen ? 'open' : 'closed');
      });
    else if (a === 'toggle-right') update((s) => (s.rightOpen = !s.rightOpen));
    else if (a === 'follow') {
      const t = selectedTruck();
      if (state.following) map.endFollow();
      else if (t) {
        userMoved = true;
        map.startFollow(t.id);
      }
    } else if (a === 'close-truck') {
      map.endFollow();
      select({ truckId: null });
      fittedFor = null;
      update((s) => (s.mobileSheet = 'none'));
    } else if (a === 'zoom-in') {
      map.userMoved();
      map.zoomBy(1.5);
    } else if (a === 'zoom-out') {
      map.userMoved();
      map.zoomBy(1 / 1.5);
    } else if (a === 'fit-fleet') {
      map.endFollow();
      map.fit(fleet().trucks.map((x) => [x.lat, x.lng])
        .filter((p) => p[0] != null));
    } else if (a === 'fit-route') {
      map.endFollow();
      const t = selectedTruck();
      if (t) fitTruck(t);
    } else if (a === 'layers') {
      const m = r('layers');
      m.hidden = !m.hidden;
      b.setAttribute('aria-expanded', String(!m.hidden));
      if (!m.hidden) patch(m, layerMenu());
    } else if (a === 'sheet')
      update((s) => (s.mobileSheet = s.mobileSheet === b.dataset.value
        ? 'none' : b.dataset.value));
    else if (a === 'expand-sheet') update((s) => (s.mobileSheet = 'detail'));
  });
  root.addEventListener('change', (e) => {
    const k = e.target.dataset.layer;
    if (k) update((s) => (s.layers[k] = e.target.checked));
  });

  // Selecting another truck restarts its planning and next-route reads.
  const onState = async () => {
    const id = state.selection.truckId;
    if (id !== lastTruck) {
      lastTruck = id;
      if (id) {
        await sel.planning();
        sel.next();
      }
    } else if (id && state.feeds.planning.data?.truckId === id &&
      state.feeds.next.state === 'idle') sel.next();
    render();
  };
  ctx.onState = onState;
  if (isPhone() && state.selection.truckId && state.mobileSheet === 'none')
    state.mobileSheet = 'peek';
  render();
  if (state.selection.truckId) sel.planning().then(() => sel.next());
}

const isPhone = () => matchMedia('(max-width: 767px)').matches;

function isOpen(id) {
  if (state.collapsed.has(id)) return false;
  if (state.expanded.has(id)) return true;
  const t = selectedTruck();
  const cur = selectedTrip(t);
  return cur?.id === id;
}

function chips(trucks) {
  const n = (f) => filterTrucks(trucks, f, '').length;
  const c = (value, label, dot) => `<button class="chip" data-action="filter"
    data-value="${value}" data-key="chip-${value}"
    aria-pressed="${state.filter === value}">${dot ? `<span class="dot ${dot}">
    </span>` : ''}${label}<span class="count">${n(value)}</span></button>`;
  return c('all', 'All') + c('moving', 'Moving', 'moving') +
    c('stopped', 'Stopped', 'stopped') + c('attention', 'Attention',
      'attention');
}

function leftPanel(trucks, t) {
  const list = filterTrucks(trucks, state.filter, state.query);
  const f = state.feeds;
  const rows = list
    .map((x) => {
      const cur = x.trips[0];
      const nextStop = cur?.stops.find((s) => !s.isCompleted) ?? cur?.to;
      return `<tr data-action="truck" data-id="${x.id}" tabindex="0"
        data-key="row-${x.id}" aria-selected="${x.id === t?.id}">
        <td><span class="unit"><span class="dot ${x.moving ? 'moving'
          : 'stopped'}" title="${esc(x.motion)}"></span>${esc(x.unit)}${
          x.attention.length ? `<span style="color:var(--warn)" title="${esc(
            x.attention.map((a) => a.text).join(', '))}">${icon('alert',
            'sm')}</span>` : ''}</span></td>
        <td>${esc(cur?.loadNumber ?? '—')}</td>
        <td>${esc(nextStop ? location(nextStop) : '—')}</td>
      </tr>`;
    })
    .join('');
  return `<span class="sheet-handle mobile-only"></span>
    <div class="panel-head">
      <h2>Fleet · ${trucks.length}</h2>
      <button class="icon-btn mobile-only" data-action="sheet" data-value="list"
        aria-label="Close list">${icon('close')}</button>
    </div>
    <div class="chips mobile-only" style="padding:0 var(--sp-lg) var(--sp-sm)">
      ${chips(trucks)}</div>
    <div class="panel-body" data-scroll="fleet">
      ${f.locations.state === 'unavailable' && !trucks.length
        ? `<div class="empty"><h3>Fleet unavailable</h3>${esc(
          f.locations.error ?? '')}</div>`
        : !trucks.length ? '<div class="empty">Loading trucks…</div>'
        : `<table class="fleet-table">
        <thead><tr><th>Truck</th><th>Load</th><th>Next stop</th></tr></thead>
        <tbody>${rows || `<tr><td colspan="3" class="muted">No trucks match.
          </td></tr>`}</tbody></table>`}
    </div>
    <div class="panel-foot"><span>Showing ${list.length} of ${
      trucks.length}</span>${dataState(ctxMode() === 'demo' ? 'demo' : f.locations.state,
        f.locations.at ? new Date(f.locations.at).toLocaleTimeString([], {
          hour: 'numeric', minute: '2-digit', second: '2-digit' }) : '')}</div>`;
}

function rightPanel(t, trip) {
  if (!t)
    return `<div class="empty" style="margin:auto">
      <h3>No truck selected</h3>Pick a truck on the map or in the list.</div>`;
  const plan = state.feeds.planning;
  const fuel = t.summary?.state?.fuelPercent ?? t.fuelPercent;
  const tabs = `<div class="tabs" role="tablist">
      <button role="tab" data-action="tab" data-value="route" data-key="tab-r"
        aria-selected="${state.tab === 'route'}">${icon('route', 'sm')}Route
      </button>
      <button role="tab" data-action="tab" data-value="fuel" data-key="tab-f"
        aria-selected="${state.tab === 'fuel'}">${icon('fuel', 'sm')}Fuel
      </button></div>`;
  const facts = `<dl class="facts">
      ${fact('user', 'Driver', t.driver)}
      ${fact('trailer', 'Trailer', t.trailerConflict
        ? `${t.trailer ?? '—'} · conflict ${t.trailerConflict}`
        : text(t.trailer))}
      ${fact('gauge', 'Motion', t.motion)}
      ${fact('clock', 'Duty', dutyLabel(t.hos?.currentDutyStatus))}
      ${fact('fuel', 'Fuel', fuel != null ? `${Math.round(fuel)}%` : '—')}
      ${fact('pin', 'Location', t.place ?? '—')}
    </dl>`;
  const body = state.tab === 'fuel' ? fuelTab(t) : `
    ${attentionList(t)}
    ${hos(t)}
    <div class="section-title"><h3>Trips · ${t.trips.length}</h3>
      ${ctxMode() === 'demo' ? '' : dataState(plan.state === 'idle'
        ? 'loading' : plan.state)}</div>
    <ol class="trip-list">${t.trips.map((x) => tripCard(t, x, isOpen(x.id)))
      .join('') || '<li class="muted">No trips on the board.</li>'}</ol>`;
  return `<span class="sheet-handle mobile-only" data-action="expand-sheet">
    </span>
    <div class="truck-head">
      <div style="flex:1;min-width:0">
        <span class="eyebrow">Truck</span>
        <h2>${esc(t.unit)}</h2>
        <div class="sub"><span class="dot ${t.moving ? 'moving' : 'stopped'}"
          style="width:8px;height:8px;border-radius:50%"></span>${esc(
          t.motion)}</div>
      </div>
      ${tabs}
      <button class="icon-btn" data-action="close-truck" data-key="close-truck"
        aria-label="Close truck">${icon('close')}</button>
    </div>
    <div class="truck-actions">
      <button class="btn ${state.following ? 'primary' : ''}"
        data-action="follow" data-key="follow"
        aria-pressed="${state.following ? 'true' : 'false'}">
        ${icon('compass', 'sm')}Follow</button>
      <button class="btn" data-action="fit-route" data-key="fit-route-2">
        ${icon('fit', 'sm')}Fit route</button>
      <button class="btn" data-action="tab" data-value="fuel"
        data-key="fuel-2">${icon('fuel', 'sm')}Fuel</button>
    </div>
    <div class="panel-body" data-scroll="detail">${facts}${body}</div>`;
}

function fact(ic, k, v) {
  return `<div class="fact">${icon(ic)}<div><dt>${esc(k)}</dt>
    <dd title="${esc(v)}">${esc(v ?? '—')}</dd></div></div>`;
}

function fuelTab(t) {
  const s = t.summary?.state;
  const fp = s?.plan?.fuelPlan;
  const pct = s?.fuelPercent ?? t.fuelPercent;
  const money = (v) => v == null ? '—' : `$${Number(v).toFixed(2)}`;
  return `<div class="fuel-gauge ${pct != null && pct < 25 ? 'low' : ''}">
      <span class="eyebrow">Current fuel</span>
      <div class="big">${pct != null ? `${Math.round(pct)}%` : '—'}</div>
      <div class="muted">${s?.fuelUpdatedAt ? `Reported ${formatInstant(
        s.fuelUpdatedAt)}` : 'No fuel reading'}</div>
      <div class="track"><i style="width:${pct ?? 0}%"></i></div>
    </div>
    ${fp ? `<dl class="kv" style="margin:0 var(--sp-lg) var(--sp-md)">
        <dt>Starting gallons</dt><dd>${fp.startingGallons ?? '—'}</dd>
        <dt>Planned purchase</dt><dd>${fp.purchaseGallons ?? '—'} gal</dd>
        <dt>Estimated cost</dt><dd>${money(fp.purchaseCostUsd)}</dd>
      </dl>
      ${(fp.stops ?? []).map((st) => `<div class="fuel-stop">
        <span class="num-bullet current">${st.number}</span>
        <b>${esc(st.name)}</b>
        <span class="muted">${esc(st.address ?? '')} · ${Math.round(
          st.milesAhead ?? 0)} mi ahead</span>
        <span class="r"><b>+${Math.round(st.buyGallons ?? 0)} gal</b><br>
          <span class="muted">@ ${money(st.yourPrice)}</span></span>
      </div>`).join('')}`
      : '<p class="note">No fuel plan for the current trip.</p>'}
    <div style="display:flex;gap:8px;margin:0 var(--sp-lg) var(--sp-lg)">
      <button class="btn block" disabled title="Read-only concept">
        Edit plan</button>
      <button class="btn primary block" disabled
        title="Read-only concept: sending is disabled">Send fuel plan</button>
    </div>
    <p class="note">Fuel figures come from the server's plan. Editing and
      sending are disabled in this read-only concept.</p>`;
}

function mobileTabs(t) {
  const b = (v, label, ic, disabled) => `<button role="tab"
    data-action="sheet" data-value="${v}" ${disabled ? 'disabled' : ''}
    aria-selected="${state.mobileSheet === v || (v === 'detail' &&
      state.mobileSheet === 'peek')}">${icon(ic, 'sm')}${label}</button>`;
  return b('list', 'Trucks', 'list') + b('detail', t ? `Truck ${esc(t.unit)}`
    : 'Details', 'truck', !t);
}

function layerMenu() {
  const row = (k, label) => `<label><input type="checkbox" data-layer="${k}"
    ${state.layers[k] ? 'checked' : ''}> ${label}</label>`;
  return row('future', 'Future routes');
}

// This sketch map is not the product map. The real Fleet Map (Google,
// satellite at close zoom, Follow) is the integrated Client on :5180.
function mapNote() {
  return `<div class="map-pill map-pill--warn">${icon('alert', 'sm')}Layout
    sketch map (boundaries: US Census, us-atlas) · not the product map ·
    the real Google map runs at
    <a href="http://localhost:5180/fleet/map">localhost:5180</a></div>`;
}
