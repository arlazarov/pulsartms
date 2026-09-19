// k6 scenario: open browser tabs polling the API the way the Client does.
// Run: k6 run -e BASE_URL=https://staging.example -e AMFTMS_EMAIL=... -e AMFTMS_PASSWORD=... tools/load/polling.js
// Tabs (virtual users) alternate between a dispatch board and a fleet map; each polls every ten
// seconds with the same conditional requests the Client sends. Credentials come only from the
// environment and are never written to the summary.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const base = (__ENV.BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
const tabs = Number(__ENV.TABS || 20);
const duration = __ENV.DURATION || '5m';
const interval = Number(__ENV.INTERVAL_SECONDS || 10);

export const options = {
  scenarios: { tabs: { executor: 'constant-vus', vus: tabs, duration } },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{poll:board}': ['p(95)<800'],
    'http_req_duration{poll:telemetry}': ['p(95)<300'],
    'http_req_duration{poll:planning}': ['p(95)<1200'],
  },
  summaryTrendStats: ['avg', 'p(50)', 'p(95)', 'p(99)', 'max'],
};

const notModified = new Counter('amftms_not_modified');
const bodyBytes = new Trend('amftms_body_bytes');

function today() {
  return new Date().toISOString().slice(0, 10);
}

function login() {
  const response = http.post(`${base}/api/auth/login`,
    JSON.stringify({ email: __ENV.AMFTMS_EMAIL, password: __ENV.AMFTMS_PASSWORD }),
    { headers: { 'Content-Type': 'application/json' }, tags: { poll: 'login' } });
  check(response, { 'login succeeded': r => r.status === 200 });
  return response.json('accessToken');
}

function poll(name, url, state) {
  const headers = { Authorization: `Bearer ${state.token}`, 'Accept-Encoding': 'br, gzip' };
  const tag = state.tags[url];
  if (tag) headers['If-None-Match'] = tag;
  const response = http.get(url, { headers, tags: { poll: name } });
  check(response, { [`${name} ok`]: r => r.status === 200 || r.status === 304 });
  if (response.status === 304) notModified.add(1, { poll: name });
  else bodyBytes.add(response.body ? response.body.length : 0, { poll: name });
  const etag = response.headers.ETag || response.headers.Etag;
  if (etag) state.tags[url] = etag;
  return response;
}

export default function () {
  const state = { token: login(), tags: {}, trucks: [] };
  const map = __VU % 2 === 0;
  if (map) poll('fuel', `${base}/api/fuel/stations?date=${today()}`, state);
  const started = Date.now();
  const seconds = parseDuration(duration);
  while ((Date.now() - started) / 1000 < seconds) {
    const tick = Date.now();
    const board = poll('board', `${base}/api/dispatch/board?page=1&date=${today()}`, state);
    if (board.status === 200) {
      const rows = board.json('response.items') || [];
      state.trucks = rows.map(row => row.truckId).filter(Boolean);
    }
    poll('telemetry', `${base}/api/fleet/locations?wait=25${map ? '' : '&points=false'}`, state);
    if (state.trucks.length > 0) {
      const truck = state.trucks[(__VU + __ITER) % state.trucks.length];
      poll('planning', `${base}/api/fleet/trucks/${truck}/planning`, state);
    }
    const remaining = interval - (Date.now() - tick) / 1000;
    if (remaining > 0) sleep(remaining);
  }
}

function parseDuration(text) {
  const match = /^(\d+)(s|m|h)$/.exec(text);
  if (!match) return 300;
  const units = { s: 1, m: 60, h: 3600 };
  return Number(match[1]) * units[match[2]];
}
