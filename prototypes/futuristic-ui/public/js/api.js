// Transport for the two data modes. Live mode calls the existing API
// through the loopback host with the tokens from the normal sign-in; the
// tokens live only in this tab's sessionStorage. Demo mode answers the same
// routes from labelled synthetic fixtures. A session is one mode or the
// other; the two are never mixed.
import { demoResponse } from './fixtures.js';

const KEY = 'pulsr-concept-session';
let session = read();
let refreshing = null;
const listeners = new Set();

function read() {
  try {
    return JSON.parse(sessionStorage.getItem(KEY) || 'null');
  } catch {
    return null;
  }
}

function write(next) {
  session = next;
  try {
    if (next) sessionStorage.setItem(KEY, JSON.stringify(next));
    else sessionStorage.removeItem(KEY);
  } catch {
    // Storage blocked: the session lasts for this page only.
  }
  for (const fn of listeners) fn(session);
}

export const getSession = () => session;
export const onSession = (fn) => listeners.add(fn);

export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

export async function signIn(email, password) {
  const r = await fetch('/api/auth/login', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  if (r.status === 401) throw new ApiError(401, 'Email or password is wrong.');
  if (!r.ok) throw new ApiError(r.status, `Sign-in failed (${r.status}).`);
  const auth = await r.json();
  if (!auth?.accessToken || !auth?.refreshToken)
    throw new ApiError(500, 'The sign-in response had no tokens.');
  write({
    mode: 'live',
    accessToken: auth.accessToken,
    refreshToken: auth.refreshToken,
  });
  try {
    const me = await request('GET', '/api/auth/me');
    write({ ...session, user: { name: me?.name, email: me?.email } });
  } catch {
    // The profile is decoration; the session is valid without it.
  }
}

export function startDemo() {
  write({ mode: 'demo', user: { name: 'Demo fixtures' } });
}

export async function signOut() {
  const current = session;
  write(null);
  if (current?.mode !== 'live') return;
  try {
    await fetch('/api/auth/logout', {
      method: 'POST',
      headers: { authorization: `Bearer ${current.accessToken}` },
    });
  } catch {
    // Local sign-out still stands; the server token expires on its own.
  }
}

async function refresh() {
  refreshing ??= (async () => {
    const token = session?.refreshToken;
    if (!token) return false;
    const r = await fetch('/api/auth/refresh', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ refreshToken: token }),
    });
    if (!r.ok) return false;
    const auth = await r.json();
    if (!auth?.accessToken || !auth?.refreshToken) return false;
    write({
      ...session,
      accessToken: auth.accessToken,
      refreshToken: auth.refreshToken,
    });
    return true;
  })().finally(() => {
    refreshing = null;
  });
  return refreshing;
}

export async function request(method, path, body, signal) {
  if (!session) throw new ApiError(401, 'Signed out');
  if (session.mode === 'demo') {
    await new Promise((r) => setTimeout(r, 120));
    if (signal?.aborted) throw new DOMException('Aborted', 'AbortError');
    return demoResponse(method, path, body);
  }
  const send = () =>
    fetch(path, {
      method,
      signal,
      headers: {
        authorization: `Bearer ${session.accessToken}`,
        accept: 'application/json',
        'x-route-geometry': 'encoded',
        ...(body ? { 'content-type': 'application/json' } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
    });
  let r = await send();
  if (r.status === 401 && (await refresh())) r = await send();
  if (r.status === 401) {
    write(null);
    throw new ApiError(401, 'Your session ended. Sign in again.');
  }
  if (!r.ok) throw new ApiError(r.status, `Request failed (${r.status})`);
  const json = r.status === 204 ? null : await r.json();
  // Most endpoints wrap results as { success, response, errors }.
  if (json && typeof json === 'object' && 'success' in json) {
    if (!json.success)
      throw new ApiError(r.status, json.errors?.[0] ?? 'Request failed');
    return json.response;
  }
  return json;
}
