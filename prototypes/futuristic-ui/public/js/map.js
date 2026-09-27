// Vector fleet map. The basemap is public boundary data (us-atlas and
// world-atlas from jsDelivr); no map provider key is used. Routes live in a
// scaled layer; markers, stops and labels are drawn in screen space so they
// keep a constant size at every zoom.
import {
  labelPoint,
  pathFromRings,
  project,
  topoFeatures,
} from './geo.js';

const NS = 'http://www.w3.org/2000/svg';
const ATLAS = 'https://cdn.jsdelivr.net/npm/us-atlas@3/states-10m.json';
const WORLD = 'https://cdn.jsdelivr.net/npm/world-atlas@2/countries-50m.json';

// Static geography for orientation labels only.
const CITIES = [
  ['Chicago', 41.88, -87.63, 1], ['New York', 40.71, -74.01, 1],
  ['Atlanta', 33.75, -84.39, 1], ['Dallas', 32.78, -96.8, 1],
  ['Detroit', 42.33, -83.05, 1], ['Philadelphia', 39.95, -75.17, 1],
  ['Washington', 38.9, -77.04, 1], ['Nashville', 36.16, -86.78, 1],
  ['St. Louis', 38.63, -90.2, 1], ['Charlotte', 35.23, -80.84, 1],
  ['Boston', 42.36, -71.06, 1], ['Memphis', 35.15, -90.05, 1],
  ['Houston', 29.76, -95.37, 1], ['Minneapolis', 44.98, -93.27, 1],
  ['Toronto', 43.65, -79.38, 1], ['Montreal', 45.5, -73.57, 1],
  ['Jacksonville', 30.33, -81.66, 1], ['Denver', 39.74, -104.99, 1],
  ['Pittsburgh', 40.44, -79.99, 2], ['Cleveland', 41.5, -81.69, 2],
  ['Columbus', 39.96, -83.0, 2], ['Indianapolis', 39.77, -86.16, 2],
  ['Louisville', 38.25, -85.76, 2], ['Cincinnati', 39.1, -84.51, 2],
  ['Knoxville', 35.96, -83.92, 2], ['Richmond', 37.54, -77.44, 2],
  ['Baltimore', 39.29, -76.61, 2], ['Buffalo', 42.89, -78.88, 2],
  ['Albany', 42.65, -73.76, 2], ['Raleigh', 35.78, -78.64, 2],
  ['Birmingham', 33.52, -86.8, 2], ['Kansas City', 39.1, -94.58, 2],
  ['Little Rock', 34.75, -92.29, 2], ['Milwaukee', 43.04, -87.91, 2],
  ['Savannah', 32.08, -81.09, 2], ['Orlando', 28.54, -81.38, 2],
  ['Miami', 25.76, -80.19, 2], ['New Orleans', 29.95, -90.07, 2],
  ['Oklahoma City', 35.47, -97.52, 2], ['Omaha', 41.26, -95.93, 2],
  ['Harrisburg', 40.27, -76.88, 3], ['Syracuse', 43.05, -76.15, 3],
  ['Hartford', 41.76, -72.68, 3], ['Norfolk', 36.85, -76.29, 3],
  ['Greensboro', 36.07, -79.79, 3], ['Columbia', 34.0, -81.03, 3],
  ['Chattanooga', 35.05, -85.31, 3], ['Toledo', 41.66, -83.56, 3],
  ['Lexington', 38.04, -84.5, 3], ['Allentown', 40.6, -75.49, 3],
  ['Goshen', 41.4, -74.32, 3], ['Newark', 40.74, -74.17, 3],
];

let atlasPromise = null;
function loadAtlas() {
  atlasPromise ??= Promise.all([
    fetch(ATLAS).then((r) => (r.ok ? r.json() : Promise.reject(r.status))),
    fetch(WORLD)
      .then((r) => (r.ok ? r.json() : null))
      .catch(() => null),
  ]).catch((e) => {
    atlasPromise = null;
    throw e;
  });
  return atlasPromise;
}

const el = (name, attrs = {}, parent) => {
  const n = document.createElementNS(NS, name);
  for (const [k, v] of Object.entries(attrs))
    if (v !== undefined && v !== null) n.setAttribute(k, v);
  parent?.appendChild(n);
  return n;
};

export class FleetMapView {
  constructor(host, handlers) {
    this.host = host;
    this.h = handlers;
    this.view = { x: 0, y: 0, k: 1 };
    this.data = { trucks: [], routes: [], stops: [], selectedId: null };
    this.pointers = new Map();
    this.svg = el('svg', {
      class: 'map-svg',
      role: 'application',
      'aria-label':
        'Fleet map. Arrow keys pan, plus and minus zoom, Tab reaches trucks.',
      tabindex: '0',
    });
    host.prepend(this.svg);
    const defs = el('defs', {}, this.svg);
    const pat = el('pattern', {
      id: 'grid',
      width: 48,
      height: 48,
      patternUnits: 'userSpaceOnUse',
    }, defs);
    el('path', {
      d: 'M48 0H0V48',
      fill: 'none',
      stroke: 'var(--map-grid)',
      'stroke-width': 1,
    }, pat);
    el('rect', { width: '100%', height: '100%', fill: 'url(#grid)' },
      this.svg);
    this.world = el('g', {}, this.svg);
    this.base = el('g', {}, this.world);
    this.routeLayer = el('g', {}, this.world);
    this.labels = el('g', {}, this.svg);
    this.overlay = el('g', {}, this.svg);
    this.bind();
    this.resize = new ResizeObserver(() => this.onResize());
    this.resize.observe(host);
    this.basemap();
  }

  destroy() {
    this.resize.disconnect();
    this.svg.remove();
  }

  async basemap() {
    try {
      const [us, world] = await loadAtlas();
      if (!this.svg.isConnected) return;
      if (world) {
        const inView = (lng, lat) =>
          lng > -140 && lng < -50 && lat > 14 && lat < 62;
        for (const c of topoFeatures(world, 'countries')) {
          if (!['124', '484', '192', '044'].includes(String(c.id))) continue;
          el('path', {
            class: 'foreign',
            d: pathFromRings(c.polygons, inView),
          }, this.base);
        }
      }
      const states = topoFeatures(us, 'states');
      this.stateLabels = [];
      for (const s of states) {
        el('path', { class: 'state', d: pathFromRings(s.polygons) },
          this.base);
        const p = labelPoint(s.polygons);
        if (p) this.stateLabels.push([s.name, p]);
      }
      el('path', {
        class: 'nation',
        d: pathFromRings(topoFeatures(us, 'nation')[0].polygons),
      }, this.base);
      this.draw();
    } catch {
      this.h.onBasemapError?.();
    }
  }

  bind() {
    const s = this.svg;
    s.addEventListener('wheel', (e) => {
      e.preventDefault();
      this.userMoved();
      const r = s.getBoundingClientRect();
      this.zoomAt(Math.exp(-e.deltaY * 0.0015), e.clientX - r.left,
        e.clientY - r.top);
    }, { passive: false });
    s.addEventListener('pointerdown', (e) => {
      if (e.target.closest('[data-hit]')) return;
      try {
        s.setPointerCapture(e.pointerId);
      } catch {
        // Pointer already released; the drag still works without capture.
      }
      this.pointers.set(e.pointerId, [e.clientX, e.clientY]);
      this.moved = 0;
    });
    s.addEventListener('pointermove', (e) => {
      const prev = this.pointers.get(e.pointerId);
      if (!prev) return;
      const cur = [e.clientX, e.clientY];
      if (this.pointers.size === 1) {
        const dx = cur[0] - prev[0];
        const dy = cur[1] - prev[1];
        this.moved += Math.abs(dx) + Math.abs(dy);
        if (this.moved > 3) s.classList.add('dragging');
        if (this.moved > 3) this.userMoved();
        this.view.x -= dx / this.view.k;
        this.view.y -= dy / this.view.k;
        this.pointers.set(e.pointerId, cur);
        this.schedule();
      } else if (this.pointers.size === 2) {
        const [a, b] = [...this.pointers.values()];
        const other = a === prev ? b : a;
        const before = Math.hypot(prev[0] - other[0], prev[1] - other[1]);
        const after = Math.hypot(cur[0] - other[0], cur[1] - other[1]);
        const r = s.getBoundingClientRect();
        this.moved += 10;
        this.pointers.set(e.pointerId, cur);
        if (before > 0)
          this.zoomAt(after / before, (cur[0] + other[0]) / 2 - r.left,
            (cur[1] + other[1]) / 2 - r.top);
      }
    });
    const end = (e) => {
      if (!this.pointers.has(e.pointerId)) return;
      this.pointers.delete(e.pointerId);
      s.classList.remove('dragging');
      if (this.pointers.size === 0 && this.moved < 4 && e.type === 'pointerup')
        this.h.onBackground?.();
    };
    s.addEventListener('pointerup', end);
    s.addEventListener('pointercancel', end);
    s.addEventListener('click', (e) => {
      const hit = e.target.closest('[data-hit]');
      if (!hit) return;
      this.activate(hit);
    });
    s.addEventListener('keydown', (e) => {
      const hit = e.target.closest?.('[data-hit]');
      if (hit && (e.key === 'Enter' || e.key === ' ')) {
        e.preventDefault();
        this.activate(hit);
        return;
      }
      const step = 60 / this.view.k;
      const keys = {
        ArrowLeft: [-step, 0],
        ArrowRight: [step, 0],
        ArrowUp: [0, -step],
        ArrowDown: [0, step],
      };
      if (keys[e.key]) {
        e.preventDefault();
        this.userMoved();
        this.view.x += keys[e.key][0];
        this.view.y += keys[e.key][1];
        this.schedule();
      } else if (e.key === '+' || e.key === '=') {
        this.userMoved();
        this.zoomBy(1.4);
      } else if (e.key === '-' || e.key === '_') {
        this.userMoved();
        this.zoomBy(1 / 1.4);
      }
    });
  }

  // Follow mirrors the production truck camera (Scripts/fleetMap/trucks/
  // truckCamera.ts): capture a screen anchor when Follow starts, keep the
  // followed truck on it for every position frame, and end on a user drag
  // or zoom. Fitting or selection changes end it through the page.
  userMoved() {
    this.h.onPan?.();
    if (this.followingId) this.endFollow();
  }

  endFollow() {
    if (!this.followingId) return;
    this.followingId = null;
    this.followAt = null;
    this.h.onFollowChange?.(false);
  }

  startFollow(id) {
    const t = this.data.trucks.find((x) => x.id === id);
    if (!t || t.lat == null) return false;
    const [w, h] = this.size();
    this.followingId = id;
    this.followAnchor = [w / 2, h / 2];
    this.followAt = null;
    this.keepFollowed(true);
    this.h.onFollowChange?.(true);
    return true;
  }

  keepFollowed(force) {
    if (!this.followingId) return;
    const t = this.data.trucks.find((x) => x.id === this.followingId);
    if (!t || t.lat == null) return;
    if (!force && this.followAt?.[0] === t.lat && this.followAt?.[1] === t.lng)
      return;
    this.followAt = [t.lat, t.lng];
    cancelAnimationFrame(this.anim);
    const [x, y] = project(t.lng, t.lat);
    if (force && this.view.k < 3) this.view.k = 3;
    this.view.x = x - this.followAnchor[0] / this.view.k;
    this.view.y = y - this.followAnchor[1] / this.view.k;
    this.schedule();
  }

  activate(hit) {
    const { hit: kind, id, trip } = hit.dataset;
    if (kind === 'truck') this.h.onTruck?.(id);
    if (kind === 'stop') this.h.onStop?.(trip, id);
    if (kind === 'route') this.h.onTrip?.(id);
  }

  size() {
    const r = this.host.getBoundingClientRect();
    return [r.width || 1, r.height || 1];
  }

  onResize() {
    const [w, h] = this.size();
    if (this.followingId) {
      this.followAnchor = [w / 2, h / 2];
      this.keepFollowed(true);
    }
    if (!this.sized && w > 10 && h > 10) {
      this.sized = true;
      if (this.pendingFit) this.fit(this.pendingFit, true);
      else this.fitDefault();
    }
    this.schedule();
  }

  zoomAt(f, sx, sy) {
    const k = Math.min(40, Math.max(0.35, this.view.k * f));
    const wx = this.view.x + sx / this.view.k;
    const wy = this.view.y + sy / this.view.k;
    this.view.k = k;
    this.view.x = wx - sx / k;
    this.view.y = wy - sy / k;
    this.schedule();
  }

  zoomBy(f) {
    const [w, h] = this.size();
    this.zoomAt(f, w / 2, h / 2);
  }

  fitDefault() {
    const pts = this.data.trucks.map((t) => [t.lat, t.lng]);
    this.fit(pts.length ? pts : [[25, -100], [47, -70]]);
  }

  // Fit [lat, lng] points, with room for the floating controls.
  fit(points, immediate) {
    if (!points?.length) return;
    if (!this.sized) {
      this.pendingFit = points;
      return;
    }
    this.pendingFit = null;
    const xy = points.map(([lat, lng]) => project(lng, lat));
    const xs = xy.map((p) => p[0]);
    const ys = xy.map((p) => p[1]);
    let [x0, x1, y0, y1] = [Math.min(...xs), Math.max(...xs),
      Math.min(...ys), Math.max(...ys)];
    const minSpan = 60;
    if (x1 - x0 < minSpan) [x0, x1] = [(x0 + x1) / 2 - 30, (x0 + x1) / 2 + 30];
    if (y1 - y0 < minSpan) [y0, y1] = [(y0 + y1) / 2 - 30, (y0 + y1) / 2 + 30];
    const [w, h] = this.size();
    const pad = { l: 64, r: 72, t: 64, b: 56 };
    const k = Math.min(
      (w - pad.l - pad.r) / (x1 - x0),
      (h - pad.t - pad.b) / (y1 - y0),
      12
    );
    const target = {
      k,
      x: (x0 + x1) / 2 - (w + pad.l - pad.r) / 2 / k,
      y: (y0 + y1) / 2 - (h + pad.t - pad.b) / 2 / k,
    };
    const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (immediate || reduce) {
      this.view = target;
      this.schedule();
      return;
    }
    const from = { ...this.view };
    const t0 = performance.now();
    cancelAnimationFrame(this.anim);
    const step = (t) => {
      const p = Math.min(1, (t - t0) / 380);
      const e = 1 - (1 - p) ** 3;
      const lk = Math.exp(Math.log(from.k) + (Math.log(target.k) -
        Math.log(from.k)) * e);
      const [cw, ch] = [w / 2 / from.k, h / 2 / from.k];
      const [tw, th] = [w / 2 / target.k, h / 2 / target.k];
      const cx = from.x + cw + (target.x + tw - from.x - cw) * e;
      const cy = from.y + ch + (target.y + th - from.y - ch) * e;
      this.view = { k: lk, x: cx - w / 2 / lk, y: cy - h / 2 / lk };
      this.draw();
      if (p < 1) this.anim = requestAnimationFrame(step);
    };
    this.anim = requestAnimationFrame(step);
  }

  schedule() {
    if (this.raf) return;
    this.raf = requestAnimationFrame(() => {
      this.raf = 0;
      this.draw();
    });
  }

  toScreen(lat, lng) {
    const [x, y] = project(lng, lat);
    return [(x - this.view.x) * this.view.k, (y - this.view.y) * this.view.k];
  }

  setData(data) {
    const routesChanged = data.routeKey !== this.data.routeKey;
    this.data = data;
    if (routesChanged) this.drawRoutes();
    this.keepFollowed(false);
    if (!this.sized) this.pendingFit ??= null;
    this.schedule();
  }

  drawRoutes() {
    this.routeLayer.replaceChildren();
    const order = { later: 0, upcoming: 1, next: 2, current: 3 };
    const routes = [...this.data.routes].sort(
      (a, b) => (order[a.phase] ?? 0) - (order[b.phase] ?? 0) +
        (a.focus ? 10 : 0) - (b.focus ? 10 : 0)
    );
    for (const r of routes) {
      const d = r.points
        .map(([lat, lng], i) => {
          const [x, y] = project(lng, lat);
          return `${i ? 'L' : 'M'}${x.toFixed(2)},${y.toFixed(2)}`;
        })
        .join('');
      if (!d) continue;
      const cls = `route ${r.phase}${r.dim ? ' dim' : ''}${
        r.focus ? ' focus' : ''}`;
      if (r.phase === 'current') el('path', { class: 'route glow', d },
        this.routeLayer);
      el('path', { class: cls, d }, this.routeLayer);
      const hit = el('path', {
        class: 'route-hit',
        d,
        'data-hit': 'route',
        'data-id': r.id,
      }, this.routeLayer);
      el('title', {}, hit).textContent = r.title ?? '';
    }
  }

  draw() {
    const { x, y, k } = this.view;
    this.world.setAttribute('transform',
      `scale(${k}) translate(${-x} ${-y})`);
    this.drawLabels();
    this.drawOverlay();
    this.h.onViewChange?.(this.view);
  }

  drawLabels() {
    this.labels.replaceChildren();
    const [w, h] = this.size();
    const k = this.view.k;
    if (this.stateLabels && k > 0.9 && k < 9) {
      for (const [name, [px, py]] of this.stateLabels) {
        const sx = (px - this.view.x) * k;
        const sy = (py - this.view.y) * k;
        if (sx < 0 || sy < 0 || sx > w || sy > h) continue;
        el('text', { class: 'state-label', x: sx, y: sy }, this.labels)
          .textContent = name;
      }
    }
    if (!this.data.layers?.cities) return;
    // Keep city names clear of stop and truck markers.
    const taken = [
      ...this.data.stops.map((p) => this.toScreen(p.lat, p.lng)),
      ...this.data.trucks
        .filter((t) => t.lat != null)
        .map((t) => this.toScreen(t.lat, t.lng)),
    ];
    const clear = (sx, sy) =>
      !taken.some(([x, y]) => Math.abs(x - sx - 24) < 44 &&
        Math.abs(y - sy) < 16);
    const tier = k < 1.6 ? 1 : k < 3.2 ? 2 : 3;
    for (const [name, lat, lng, t] of CITIES) {
      if (t > tier) continue;
      const [sx, sy] = this.toScreen(lat, lng);
      if (sx < -20 || sy < -20 || sx > w + 20 || sy > h + 20) continue;
      if (!clear(sx, sy)) continue;
      const g = el('g', { class: 'city' }, this.labels);
      el('circle', { cx: sx, cy: sy, r: 2.2 }, g);
      el('text', { x: sx + 6, y: sy + 4 }, g).textContent = name;
    }
  }

  drawOverlay() {
    this.overlay.replaceChildren();
    const [w, h] = this.size();
    const sel = this.data.selectedId;
    const inView = (sx, sy) => sx > -40 && sy > -40 && sx < w + 40 &&
      sy < h + 40;
    for (const s of this.data.stops) {
      const [sx, sy] = this.toScreen(s.lat, s.lng);
      if (!inView(sx, sy)) continue;
      const g = el('g', {
        class: 'stop',
        transform: `translate(${sx.toFixed(1)} ${sy.toFixed(1)})`,
        'data-hit': 'stop',
        'data-id': s.id,
        'data-trip': s.tripId,
        'data-selected': s.selected ? 'true' : 'false',
        tabindex: '0',
        role: 'button',
        'aria-label': s.aria,
        style: `color: var(--${s.phase})`,
      }, this.overlay);
      el('circle', { class: 'sel', r: 17 }, g);
      el('circle', {
        class: 'bg',
        r: s.major ? 11 : 8,
        stroke: 'currentColor',
        opacity: s.dim ? 0.55 : 1,
      }, g);
      el('text', { fill: 'currentColor', 'font-size': s.major ? 11 : 9 }, g)
        .textContent = s.label;
    }
    const trucks = [...this.data.trucks].sort(
      (a, b) => (a.id === sel) - (b.id === sel)
    );
    for (const t of trucks) {
      if (t.lat == null || t.lng == null) continue;
      if (t.id !== sel && !this.data.layers?.others) continue;
      const [sx, sy] = this.toScreen(t.lat, t.lng);
      if (!inView(sx, sy)) continue;
      const selected = t.id === sel;
      const g = el('g', {
        class: `truck${selected ? ' selected' : ''}${
          t.moving ? '' : ' stopped'}${sel && !selected ? ' quiet' : ''}`,
        transform: `translate(${sx.toFixed(1)} ${sy.toFixed(1)})`,
        'data-hit': 'truck',
        'data-id': t.id,
        tabindex: '0',
        role: 'button',
        'aria-label': `Truck ${t.unit}, ${t.motion}${
          selected ? ', selected' : ''}`,
        'aria-pressed': selected ? 'true' : 'false',
      }, this.overlay);
      if (selected) {
        el('circle', { class: 'ring fill', r: 26 }, g);
        el('circle', { class: 'ring r3', r: 34 }, g);
        el('circle', { class: 'ring r2', r: 25 }, g);
        el('circle', { class: 'ring r1', r: 16 }, g);
        el('circle', { class: 'ring pulse', r: 22 }, g);
      }
      const s = selected ? 1.35 : 1;
      el('path', {
        class: 'arrow',
        d: 'M0,-9 L7,7 L0,3.2 L-7,7 Z',
        transform: `rotate(${t.heading ?? 0}) scale(${s})`,
      }, g);
      const label = String(t.unit);
      const lw = label.length * 7 + 12;
      const lab = el('g', {
        class: 'label',
        transform: `translate(${selected ? 20 : 12} -10)`,
      }, g);
      el('rect', { width: lw, height: 20, rx: 6 }, lab);
      el('text', { x: 6, y: 10 }, lab).textContent = label;
    }
  }
}
