// Local-only host for the futuristic UI concept.
//
// Serves the static prototype and forwards a fixed allow-list of existing,
// read-only API routes plus the normal sign-in/refresh/logout routes to the
// real API. Every other API request is refused here, so the concept cannot
// send messages, change loads or trigger payments even with a valid session.
// No credentials are stored: the browser holds the session tokens it received
// from the normal sign-in, and this process only relays them.
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

// CONCEPT_ROOT serves a Client publish (its wwwroot) instead of the concept:
// the real Fleet Map and Dispatch, on the provider map, with this host's
// read-only API allow-list.
const root = process.env.CONCEPT_ROOT
  ? process.env.CONCEPT_ROOT.replace(/\/?$/, '/')
  : fileURLToPath(new URL('./public/', import.meta.url));
const host = '127.0.0.1';
const port = Number(process.env.PORT ?? 5179);
const upstream = new URL(
  process.env.PULSR_API_ORIGIN ?? 'https://amftms.web.app'
);

import { routes as allowed } from './read-routes.mjs';

const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.json': 'application/json',
  '.png': 'image/png',
  '.wasm': 'application/wasm',
  '.dat': 'application/octet-stream',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.webmanifest': 'application/manifest+json',
};

const forwardHeaders = [
  'authorization',
  'content-type',
  'accept',
  'x-route-geometry',
];

function isAllowed(method, path) {
  return allowed.some(([m, p]) => m === method && p.test(path));
}

async function proxy(req, res, url) {
  if (!isAllowed(req.method, url.pathname)) {
    res.writeHead(403, { 'content-type': 'application/json' });
    res.end(
      JSON.stringify({
        error: 'blocked-by-concept',
        detail: 'The concept host forwards read-only routes only.',
      })
    );
    console.log(`blocked ${req.method} ${url.pathname}`);
    return;
  }
  const headers = {};
  for (const h of forwardHeaders)
    if (req.headers[h]) headers[h] = req.headers[h];
  const chunks = [];
  for await (const c of req) chunks.push(c);
  const target = new URL(url.pathname + url.search, upstream);
  const controller = new AbortController();
  req.on('close', () => {
    if (!res.writableEnded) controller.abort();
  });
  try {
    const r = await fetch(target, {
      method: req.method,
      headers,
      body: chunks.length ? Buffer.concat(chunks) : undefined,
      signal: controller.signal,
      redirect: 'manual',
    });
    const out = { 'cache-control': 'no-store' };
    const ct = r.headers.get('content-type');
    if (ct) out['content-type'] = ct;
    res.writeHead(r.status, out);
    res.end(Buffer.from(await r.arrayBuffer()));
  } catch (e) {
    if (controller.signal.aborted) return;
    res.writeHead(502, { 'content-type': 'application/json' });
    res.end(JSON.stringify({ error: 'upstream-unavailable' }));
    console.log(`upstream failure ${req.method} ${url.pathname}: ${e.code}`);
  }
}

async function serveStatic(res, pathname) {
  let rel = normalize(decodeURIComponent(pathname)).replace(/^([/\\])+/, '');
  if (rel.startsWith('..')) rel = '';
  let file = join(root, rel || 'index.html');
  try {
    if ((await stat(file)).isDirectory()) file = join(file, 'index.html');
  } catch {
    file = join(root, 'index.html');
  }
  try {
    const body = await readFile(file);
    res.writeHead(200, {
      'content-type': types[extname(file)] ?? 'application/octet-stream',
      'cache-control': 'no-cache',
    });
    res.end(body);
  } catch {
    res.writeHead(404);
    res.end('Not found');
  }
}

createServer((req, res) => {
  const url = new URL(req.url, `http://${host}:${port}`);
  if (url.pathname.startsWith('/api/')) return proxy(req, res, url);
  return serveStatic(res, url.pathname);
}).listen(port, host, () => {
  console.log(`PulsR concept on http://localhost:${port}`);
  console.log(`API upstream ${upstream.origin} (read-only allow-list)`);
});
