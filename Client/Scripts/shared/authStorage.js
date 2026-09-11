const sessionKey = 'auth_session';
const lockName = 'amftms:auth-session';

function valid(session) {
  return session !== null && typeof session === 'object'
    && typeof session.Id === 'string'
    && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(session.Id)
    && session.Id !== '00000000-0000-0000-0000-000000000000'
    && typeof session.AccessToken === 'string' && session.AccessToken.trim().length > 0
    && typeof session.RefreshToken === 'string' && session.RefreshToken.trim().length > 0;
}

function parse(json) {
  try {
    const session = JSON.parse(json);
    return valid(session) ? {Id: session.Id.toLowerCase(), AccessToken: session.AccessToken, RefreshToken: session.RefreshToken} : null;
  } catch { return null; }
}

function write(session) {
  localStorage.setItem(sessionKey, JSON.stringify(session));
  localStorage.removeItem('access_token');
  localStorage.removeItem('refresh_token');
}

function read() {
  const json = localStorage.getItem(sessionKey);
  if (json !== null) return parse(json);
  const AccessToken = localStorage.getItem('access_token');
  const RefreshToken = localStorage.getItem('refresh_token');
  if (!AccessToken?.trim() || !RefreshToken?.trim()) return null;
  const session = {Id: crypto.randomUUID(), AccessToken, RefreshToken};
  write(session);
  return session;
}

function locked(action) {
  // The lock covers legacy migration and the entire compare-and-set across tabs.
  // An unlocked fallback can overwrite a newer login with a stale refresh.
  if (!globalThis.navigator?.locks?.request)
    throw new Error('Secure authentication storage requires Web Locks and a secure origin.');
  return navigator.locks.request(lockName, action);
}

export function readSession() {
  return locked(() => {
    const session = read();
    return session === null ? null : JSON.stringify(session);
  });
}

export function setSession(json) {
  const session = parse(json);
  if (session === null) throw new Error('Invalid authentication session.');
  return locked(() => write(session));
}

export function replaceSession(expectedJson, replacementJson) {
  const expected = parse(expectedJson);
  const replacement = replacementJson === null ? null : parse(replacementJson);
  if (expected === null || (replacementJson !== null && replacement === null))
    throw new Error('Invalid authentication session.');
  if (replacement !== null && replacement.Id !== expected.Id)
    throw new Error('A token refresh cannot change the login session.');
  return locked(() => {
    const current = read();
    if (current === null || current.Id !== expected.Id || current.AccessToken !== expected.AccessToken
      || current.RefreshToken !== expected.RefreshToken) return false;
    write(replacement);
    return true;
  });
}

export function clearSession(id) {
  return locked(() => {
    if (read()?.Id !== id) return false;
    write(null);
    return true;
  });
}

export function clear() { return locked(() => write(null)); }
