// SYNTHETIC DEMO FIXTURES. Used only in the explicitly labelled demo mode
// for layout checks; never merged with live responses. Shapes follow the
// Client DTOs (Models/DTO/...) so the demo exercises the live adapter.
// Every value here is invented; the page says so wherever it is shown.

const cities = {
  NSH: ['Nashville', 'TN', 36.16, -86.78],
  KNX: ['Knoxville', 'TN', 35.96, -83.92],
  RIC: ['Richmond', 'VA', 37.54, -77.44],
  BAL: ['Baltimore', 'MD', 39.29, -76.61],
  PHL: ['Philadelphia', 'PA', 39.95, -75.17],
  CHI: ['Chicago', 'IL', 41.88, -87.63],
  DET: ['Detroit', 'MI', 42.33, -83.05],
  ATL: ['Atlanta', 'GA', 33.75, -84.39],
  DAL: ['Dallas', 'TX', 32.78, -96.8],
  STL: ['St. Louis', 'MO', 38.63, -90.2],
  CLT: ['Charlotte', 'NC', 35.23, -80.84],
  MEM: ['Memphis', 'TN', 35.15, -90.05],
  IND: ['Indianapolis', 'IN', 39.77, -86.16],
  COL: ['Columbus', 'OH', 39.96, -83.0],
  CLE: ['Cleveland', 'OH', 41.5, -81.69],
  PIT: ['Pittsburgh', 'PA', 40.44, -79.99],
  NWK: ['Newark', 'NJ', 40.74, -74.17],
  BOS: ['Boston', 'MA', 42.36, -71.06],
  ALB: ['Albany', 'NY', 42.65, -73.76],
  BUF: ['Buffalo', 'NY', 42.89, -78.88],
  LOU: ['Louisville', 'KY', 38.25, -85.76],
  CIN: ['Cincinnati', 'OH', 39.1, -84.51],
  BHM: ['Birmingham', 'AL', 33.52, -86.8],
  JAX: ['Jacksonville', 'FL', 30.33, -81.66],
  SAV: ['Savannah', 'GA', 32.08, -81.09],
  RAL: ['Raleigh', 'NC', 35.78, -78.64],
  HAR: ['Harrisburg', 'PA', 40.27, -76.88],
  ALN: ['Allentown', 'PA', 40.6, -75.49],
  KCM: ['Kansas City', 'MO', 39.1, -94.58],
  LRK: ['Little Rock', 'AR', 34.75, -92.29],
  MIL: ['Milwaukee', 'WI', 43.04, -87.91],
  TOL: ['Toledo', 'OH', 41.66, -83.56],
  GOS: ['Goshen', 'NY', 41.4, -74.32],
  NOR: ['Norfolk', 'VA', 36.85, -76.29],
  GSO: ['Greensboro', 'NC', 36.07, -79.79],
  CAE: ['Columbia', 'SC', 34.0, -81.03],
  CHA: ['Chattanooga', 'TN', 35.05, -85.31],
  SYR: ['Syracuse', 'NY', 43.05, -76.15],
  HFD: ['Hartford', 'CT', 41.76, -72.68],
  MSP: ['Minneapolis', 'MN', 44.98, -93.27],
};

const facilities = [
  'Riverside Cold Storage',
  'Summit Paper DC',
  'Keystone Foods',
  'Harbor Point Logistics',
  'Blue Ridge Distribution',
  'Northgate Steel',
  'Maple Leaf Packaging',
  'Lakeshore Grocers DC',
  'Pinecrest Building Supply',
  'Atlas Beverage',
  'Granite State Plastics',
  'Meridian Pharma DC',
];

const customers = [
  'Demo Freight Co.',
  'Sample Brokerage',
  'Example Logistics',
  'Placeholder Shippers',
];

const drivers = [
  'Demo Driver A',
  'Demo Driver B',
  'Demo Driver C',
  'Demo Driver D',
  'Demo Driver E',
  'Demo Driver F',
  'Demo Driver G',
  'Demo Driver H',
  'Demo Driver I',
  'Demo Driver J',
  'Demo Driver K',
  'Demo Driver L',
  'Demo Driver M',
  'Demo Driver N',
  'Demo Driver O',
  'Demo Driver P',
  'Demo Driver Q',
  'Demo Driver R',
  'Demo Driver S',
  'Demo Driver T',
];

// unit, trips (city pairs; first is current unless stopped-empty), progress,
// engine, duty, fuel, hos hours [break, drive, shift, cycle]
const trucks = [
  ['D-101', [['NSH', 'KNX'], ['KNX', 'RIC'], ['RIC', 'BAL'], ['BAL', 'PHL']],
    0.55, 'on', 'driving', 77, [5.2, 8.7, 10.2, 42.3]],
  ['D-102', [['CHI', 'DET'], ['DET', 'TOL'], ['TOL', 'CLE']], 0.3, 'on',
    'driving', 64, [6.5, 9.1, 11.5, 50.2]],
  ['D-103', [['IND', 'CIN'], ['CIN', 'LOU'], ['LOU', 'NSH']], 0.7, 'on',
    'driving', 41, [2.1, 3.4, 4.5, 21.3]],
  ['D-104', [['ATL', 'CLT'], ['CLT', 'RAL'], ['RAL', 'NOR']], 0.2, 'on',
    'driving', 88, [7.4, 10.1, 12.9, 60.1]],
  ['D-105', [['DAL', 'LRK'], ['LRK', 'MEM']], 0.1, 'off', 'sleeperBerth',
    55, [8, 11, 14, 33.4]],
  ['D-106', [['STL', 'IND'], ['IND', 'COL'], ['COL', 'PIT'], ['PIT', 'HAR']],
    0.45, 'on', 'driving', 23, [4.2, 6.8, 7.9, 29.9]],
  ['D-107', [['CLT', 'ATL'], ['ATL', 'BHM']], 0.8, 'on', 'driving', 59,
    [1.4, 2.2, 3.1, 12.6]],
  ['D-108', [['CLE', 'BUF'], ['BUF', 'SYR'], ['SYR', 'ALB']], 0.35, 'on',
    'driving', 70, [5.9, 7.5, 9.8, 44.8]],
  ['D-109', [['MEM', 'NSH'], ['NSH', 'CHA']], 0, 'off', 'offDuty', 91,
    [8, 11, 14, 58.2]],
  ['D-110', [['PIT', 'PHL'], ['PHL', 'NWK'], ['NWK', 'HFD'], ['HFD', 'BOS']],
    0.6, 'on', 'driving', 47, [3.8, 5.6, 6.9, 38.1]],
  ['D-111', [['JAX', 'SAV'], ['SAV', 'CAE'], ['CAE', 'CLT']], 0.5, 'on',
    'driving', 66, [6.2, 9.4, 10.5, 51.7]],
  ['D-112', [['KCM', 'STL'], ['STL', 'CHI']], 0.25, 'on', 'driving', 14,
    [5.1, 7.7, 8.2, 26.4]],
  ['D-113', [['MIL', 'CHI'], ['CHI', 'IND']], 0, 'idle', 'onDuty', 72,
    [7.8, 10.4, 12.1, 47.5]],
  ['D-114', [['BAL', 'HAR'], ['HAR', 'ALN'], ['ALN', 'GOS']], 0.65, 'on',
    'driving', 52, [4.4, 6.1, 7.3, 35.2]],
  ['D-115', [['DET', 'COL'], ['COL', 'CIN']], 0.4, 'on', 'driving', 80,
    [6.9, 9.8, 11.2, 55.9]],
  ['D-116', [['RAL', 'GSO']], 0, 'off', 'sleeperBerth', 38,
    [8, 11, 14, 18.7]],
  ['D-117', [['LOU', 'KNX'], ['KNX', 'ATL'], ['ATL', 'JAX']], 0.15, 'on',
    'driving', 95, [7.1, 10.6, 13.4, 62.3]],
  ['D-118', [['BOS', 'ALB'], ['ALB', 'SYR']], 0, 'off', 'offDuty', 61,
    [8, 11, 14, 40.4]],
  ['D-119', [['CHA', 'BHM'], ['BHM', 'MEM'], ['MEM', 'LRK']], 0.5, 'on',
    'driving', 44, [0.6, 1.9, 2.4, 9.1]],
  ['D-120', [['GOS', 'NWK']], 0, 'on', 'onDuty', 83, [8, 11, 14, 66.0]],
];

const now = () => new Date();
const iso = (d) => d.toISOString();
const ymd = (d) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(
    d.getDate()
  ).padStart(2, '0')}`;
const hm = (d) =>
  `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(
    2,
    '0'
  )}:00`;
const uuid = (n) =>
  `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

function miles(a, b) {
  const r = Math.PI / 180;
  const dLat = (b[2] - a[2]) * r;
  const dLng = (b[3] - a[3]) * r;
  const h =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(a[2] * r) * Math.cos(b[2] * r) * Math.sin(dLng / 2) ** 2;
  return 3959 * 2 * Math.asin(Math.sqrt(h)) * 1.18;
}

// A gently curved polyline between two cities.
function curve(a, b, seed) {
  const pts = [];
  const steps = 28;
  const dx = b[3] - a[3];
  const dy = b[2] - a[2];
  const bend = (seed % 2 ? 1 : -1) * 0.12;
  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    const off = Math.sin(Math.PI * t) * bend;
    const wobble = Math.sin(t * Math.PI * 5 + seed) * 0.02;
    pts.push([
      a[2] + dy * t + dx * (off + wobble),
      a[3] + dx * t - dy * (off + wobble),
    ]);
  }
  return pts;
}

function encode(points, precision = 6) {
  const f = 10 ** precision;
  let out = '';
  let pl = 0;
  let pg = 0;
  const enc = (v) => {
    v = v < 0 ? ~(v << 1) : v << 1;
    let s = '';
    while (v >= 0x20) {
      s += String.fromCharCode((0x20 | (v & 0x1f)) + 63);
      v >>= 5;
    }
    return s + String.fromCharCode(v + 63);
  };
  for (const [lat, lng] of points) {
    const a = Math.round(lat * f);
    const b = Math.round(lng * f);
    out += enc(a - pl) + enc(b - pg);
    pl = a;
    pg = b;
  }
  return out;
}

let cache = null;

function build() {
  const t0 = now();
  let loadNo = 9401;
  let stopNo = 1;
  const locations = [];
  const hos = {};
  const rows = [];
  const plans = {};
  const next = {};
  trucks.forEach((spec, ti) => {
    const [unit, trips, progress, engine, duty, fuel, clocks] = spec;
    const truckId = uuid(1000 + ti);
    const moving = engine === 'on' && duty === 'driving';
    const driverName = drivers[ti];
    const trailerNumber = `DT-${540 + ti}`;
    let dayOffset = 0;
    const dispatches = trips.map(([from, to], k) => {
      const a = cities[from];
      const b = cities[to];
      const m = Math.round(miles(a, b));
      const hours = m / 52;
      const start = new Date(t0);
      if (k === 0) start.setHours(start.getHours() - hours * progress - 1);
      else {
        dayOffset += 1;
        start.setDate(t0.getDate() + dayOffset);
        start.setHours(7 + ((ti + k) % 5), (ti * 13) % 60, 0, 0);
      }
      const end = new Date(start.getTime() + (hours + 1.5) * 3600000);
      if (k > 0 && end.getDate() !== start.getDate()) dayOffset += 1;
      const current = k === 0 && progress > 0;
      const late = ti === 6 && k === 0;
      const stops = [
        {
          id: uuid(50000 + stopNo++),
          sequence: 1,
          job: 'pickup',
          name: facilities[(ti + k) % facilities.length],
          address: `${100 + ti * 7} Industrial Pkwy`,
          city: a[0],
          province: a[1],
          country: 'US',
          latitude: a[2],
          longitude: a[3],
          scheduledDate: ymd(start),
          scheduledTime: hm(start),
          isWindow: false,
          isCompleted: current,
          pickedUpAt: current ? iso(start) : null,
        },
        {
          id: uuid(50000 + stopNo++),
          sequence: 2,
          job: 'delivery',
          name: facilities[(ti + k + 5) % facilities.length],
          address: `${2200 + ti * 3} Commerce Dr`,
          city: b[0],
          province: b[1],
          country: 'US',
          latitude: b[2],
          longitude: b[3],
          scheduledDate: ymd(end),
          scheduledTime: hm(new Date(end.getTime() - (late ? 3600000 : 0))),
          isWindow: k % 2 === 1,
          scheduledDate2: k % 2 === 1 ? ymd(end) : null,
          scheduledTime2:
            k % 2 === 1 ? hm(new Date(end.getTime() + 7200000)) : null,
          isCompleted: false,
        },
      ];
      const price = Math.round((m * (2.3 + ((ti + k) % 5) * 0.18)) / 25) * 25;
      return {
        id: uuid(20000 + loadNo),
        loadNumber: `DEMO-${loadNo++}`,
        orderNumber: `ORD-${70000 + ti * 10 + k}`,
        status: current ? 'in_transit' : k === 0 ? 'assigned' : 'planned',
        completed: false,
        truckId,
        truckNumber: unit,
        driverName,
        trailerNumber,
        customerName: customers[(ti + k) % customers.length],
        shipDate: ymd(start),
        deliveryDate: ymd(end),
        loadedMiles: m,
        emptyMiles: k === 0 ? 0 : Math.round(m * 0.08),
        totalMiles: m + (k === 0 ? 0 : Math.round(m * 0.08)),
        price,
        currency: 'USD',
        stops,
        _from: a,
        _to: b,
        _seed: ti * 7 + k,
        _end: end,
      };
    });
    const first = dispatches[0];
    const path = curve(first._from, first._to, first._seed);
    const at = Math.min(path.length - 1, Math.round(progress * (path.length - 1)));
    const p = path[at];
    const q = path[Math.min(path.length - 1, at + 1)];
    const heading =
      (Math.atan2(q[1] - p[1], q[0] - p[0]) * 180) / Math.PI;
    const speed = moving ? 52 + ((ti * 7) % 14) : 0;
    const hosObj = {
      breakMs: clocks[0] * 3600000,
      driveMs: clocks[1] * 3600000,
      shiftMs: clocks[2] * 3600000,
      cycleMs: clocks[3] * 3600000,
      updatedAt: iso(new Date(t0.getTime() - 120000)),
      currentDutyStatus: duty,
    };
    locations.push({
      truckId,
      unitNumber: unit,
      driverName,
      trailerNumber,
      trailerSource: 'telemetry',
      trailerConflictNumber: ti === 13 ? 'DT-599' : null,
      latitude: p[0],
      longitude: p[1],
      heading: (heading + 360) % 360,
      speed,
      engineState: engine,
      updatedAt: iso(new Date(t0.getTime() - 40000)),
      formattedLocation: `Near ${first._from[0]}, ${first._from[1]}`,
      fuelPercent: fuel,
    });
    hos[truckId] = { driverName, hos: hosObj };
    const clean = dispatches.map(
      ({ _from, _to, _seed, _end, ...d }) => d
    );
    rows.push({
      key: `truck:${truckId}`,
      truckId,
      truckNumber: unit,
      driverName,
      trailerNumber,
      speed,
      engineState: engine,
      hos: null,
      currentCycle: null,
      dispatches: clean,
    });
    const remaining = Math.round(first.loadedMiles * (1 - progress));
    const legPts = path.slice(at);
    plans[truckId] = {
      truckId,
      dispatchId: first.id,
      loadNumber: first.loadNumber,
      calculatedAt: iso(t0),
      isRefreshing: false,
      hos: hosObj,
      notices:
        ti === 18
          ? [
              {
                kind: 'cycle',
                loadNumber: first.loadNumber,
                text: 'Cycle short for the next load (demo notice)',
              },
            ]
          : [],
      state: {
        fuelPercent: fuel,
        fuelUpdatedAt: iso(t0),
        progress: {
          progressMiles: first.loadedMiles - remaining,
          remainingMiles: remaining,
          remainingSeconds: (remaining / 52) * 3600,
          distanceFromRouteMiles: ti === 9 ? 6.4 : 0.1,
          offRoute: ti === 9,
          locationStale: false,
          locationTime: iso(t0),
        },
        eta: {
          calculatedAt: iso(t0),
          stops: first.stops.map((s, i) => ({
            stopId: s.id,
            arrival: iso(
              new Date(
                first._end.getTime() -
                  (i === 0 ? first._end - new Date(s.pickedUpAt ?? t0) : 0) +
                  (ti === 6 ? 50 * 60000 : -20 * 60000)
              )
            ),
            lateMinutes: ti === 6 && i === 1 ? 50 : 0,
            timeZoneId: 'America/New_York',
          })),
        },
        plan: {
          id: uuid(90000 + ti),
          version: 1,
          dispatchId: first.id,
          truckId,
          route: {
            miles: remaining,
            legs: [{ miles: remaining, seconds: 0, points: [], path: encode(legPts) }],
          },
          stops: first.stops.map((s) => ({
            id: s.id,
            name: s.name,
            job: s.job,
            sequence: s.sequence,
            point: { latitude: s.latitude, longitude: s.longitude },
          })),
          fuelPlan:
            fuel < 60
              ? {
                  startingGallons: Math.round(fuel * 2.4),
                  purchaseGallons: 90 + ti * 3,
                  purchaseCostUsd: (90 + ti * 3) * 3.62,
                  stops: [
                    {
                      number: 1,
                      name: `Demo Travel Center #${300 + ti}`,
                      address: `I-${40 + ti} Exit ${100 + ti * 3}`,
                      point: {
                        latitude: path[Math.min(at + 8, path.length - 1)][0],
                        longitude: path[Math.min(at + 8, path.length - 1)][1],
                      },
                      milesAhead: Math.round(remaining * 0.35),
                      buyGallons: 90 + ti * 3,
                      yourPrice: 3.62,
                      sent: false,
                    },
                  ],
                }
              : null,
        },
      },
    };
    next[truckId] = {
      revision: 1,
      unchanged: false,
      labels: [],
      routes: dispatches.slice(1).map((d) => ({
        id: d.id,
        loadNumber: d.loadNumber,
        status: 'ready',
        legs: [
          {
            miles: d.loadedMiles,
            seconds: 0,
            points: [],
            path: encode(curve(d._from, d._to, d._seed)),
          },
        ],
        stops: d.stops.map((s) => ({
          id: s.id,
          latitude: s.latitude,
          longitude: s.longitude,
          job: s.job,
          name: s.name,
        })),
        deadhead: null,
        stopCount: d.stops.length,
      })),
    };
  });
  // Two loads awaiting assignment, as the board shows them.
  const unassigned = [
    ['PHL', 'BOS'],
    ['ATL', 'NSH'],
  ].map(([f, t], i) => {
    const a = cities[f];
    const b = cities[t];
    const s = new Date(t0);
    s.setDate(s.getDate() + 2 + i);
    s.setHours(9, 0, 0, 0);
    const e = new Date(s.getTime() + 9 * 3600000);
    return {
      id: uuid(29000 + i),
      loadNumber: `DEMO-${9600 + i}`,
      orderNumber: `ORD-${79000 + i}`,
      status: 'unassigned',
      completed: false,
      customerName: customers[i],
      shipDate: ymd(s),
      deliveryDate: ymd(e),
      loadedMiles: Math.round(miles(a, b)),
      stops: [
        { id: uuid(59000 + i * 2), sequence: 1, job: 'pickup',
          name: facilities[i], city: a[0], province: a[1],
          latitude: a[2], longitude: a[3], scheduledDate: ymd(s),
          scheduledTime: hm(s), isCompleted: false },
        { id: uuid(59001 + i * 2), sequence: 2, job: 'delivery',
          name: facilities[i + 3], city: b[0], province: b[1],
          latitude: b[2], longitude: b[3], scheduledDate: ymd(e),
          scheduledTime: hm(e), isCompleted: false },
      ],
    };
  });
  rows.push({
    key: 'unassigned',
    truckId: null,
    truckNumber: null,
    driverName: null,
    trailerNumber: null,
    speed: 0,
    engineState: null,
    dispatches: unassigned,
  });
  return { locations, hos, rows, plans, next };
}

function data() {
  cache ??= build();
  return cache;
}

function page(items, url) {
  const p = Number(url.searchParams.get('page') ?? 1);
  const size = Number(url.searchParams.get('pageSize') ?? 12);
  const q = (url.searchParams.get('search') ?? '').toLowerCase();
  const filtered = q
    ? items.filter((r) =>
        JSON.stringify([r.truckNumber, r.driverName, r.trailerNumber,
          r.dispatches.map((d) => d.loadNumber)]).toLowerCase().includes(q)
      )
    : items;
  const truckId = url.searchParams.get('truckId');
  const scoped = truckId
    ? filtered.filter((r) => r.truckId === truckId)
    : filtered;
  return {
    items: scoped.slice((p - 1) * size, p * size),
    page: p,
    pageSize: size,
    totalCount: scoped.length,
    totalPages: Math.max(1, Math.ceil(scoped.length / size)),
  };
}

// Demo trucks creep along their heading so Follow and marker updates can be
// checked; the movement is synthetic, like everything in this file.
const started = Date.now();
function moved(d) {
  const minutes = (Date.now() - started) / 60000;
  return d.locations.map((l) => {
    if (!l.speed) return { ...l, updatedAt: new Date().toISOString() };
    const deg = (l.speed * minutes) / 60 / 69;
    const h = (l.heading * Math.PI) / 180;
    return {
      ...l,
      latitude: l.latitude + deg * Math.cos(h),
      longitude: l.longitude + (deg * Math.sin(h)) /
        Math.cos((l.latitude * Math.PI) / 180),
      updatedAt: new Date().toISOString(),
    };
  });
}

export function demoResponse(method, path) {
  const url = new URL(path, location.origin);
  const d = data();
  const route = url.pathname;
  let m;
  if (route === '/api/fleet/locations') return { trucks: moved(d), points: [] };
  if (route === '/api/fleet/hos') return d.hos;
  if (route === '/api/dispatch/board') return page(d.rows, url);
  if (route === '/api/dispatch/board/telemetry')
    return d.locations.map((l) => ({
      truckId: l.truckId,
      speed: l.speed,
      engineState: l.engineState,
      trailerNumber: l.trailerNumber,
    }));
  if (route === '/api/dispatch/board/planning')
    return Object.values(d.plans);
  if ((m = route.match(/^\/api\/fleet\/trucks\/([^/]+)\/planning$/)))
    return d.plans[m[1]] ?? null;
  if ((m = route.match(/^\/api\/dispatch\/truck\/([^/]+)\/next-routes$/)))
    return d.next[m[1]] ?? { routes: [], revision: 0 };
  if (route === '/api/auth/me') return { name: 'Demo fixtures' };
  throw new Error(`No demo fixture for ${method} ${route}`);
}
