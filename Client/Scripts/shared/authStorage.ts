const sessionKey = 'auth_session';
const lockName = 'amftms:auth-session';

// The session as the app stores it: an id and the two tokens, all three
// present and none of them empty.
type Session = { Id: string; AccessToken: string; RefreshToken: string };

function valid(session: unknown): session is Session {
  const value = session as Partial<Session> | null;
  return (
    value !== null &&
    typeof value === 'object' &&
    typeof value.Id === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
      value.Id,
    ) &&
    value.Id !== '00000000-0000-0000-0000-000000000000' &&
    typeof value.AccessToken === 'string' &&
    value.AccessToken.trim().length > 0 &&
    typeof value.RefreshToken === 'string' &&
    value.RefreshToken.trim().length > 0
  );
}

function parse(json: string | null): Session | null {
  try {
    const session = JSON.parse(json ?? '');
    return valid(session)
      ? {
          Id: session.Id.toLowerCase(),
          AccessToken: session.AccessToken,
          RefreshToken: session.RefreshToken,
        }
      : null;
  } catch {
    return null;
  }
}

function write(session: Session | null): void {
  localStorage.setItem(sessionKey, JSON.stringify(session));
  localStorage.removeItem('access_token');
  localStorage.removeItem('refresh_token');
}

function read(): Session | null {
  const json = localStorage.getItem(sessionKey);
  if (json !== null) return parse(json);
  const AccessToken = localStorage.getItem('access_token');
  const RefreshToken = localStorage.getItem('refresh_token');
  if (!AccessToken?.trim() || !RefreshToken?.trim()) return null;
  const session = { Id: crypto.randomUUID(), AccessToken, RefreshToken };
  write(session);
  return session;
}

function locked<T>(action: () => T | Promise<T>): Promise<T> {
  // The lock covers legacy migration and the entire compare-and-set across tabs.
  // An unlocked fallback can overwrite a newer login with a stale refresh.
  if (!globalThis.navigator?.locks?.request)
    throw new Error(
      'Secure authentication storage requires Web Locks and a secure origin.',
    );
  return navigator.locks.request(lockName, action);
}

export function readSession() {
  return locked(() => {
    const session = read();
    return session === null ? null : JSON.stringify(session);
  });
}

export function setSession(json: string): Promise<void> {
  const session = parse(json);
  if (session === null) throw new Error('Invalid authentication session.');
  return locked(() => write(session));
}

// A refresh replaces one session with another only if what is stored is
// still exactly what the caller last saw.
export function replaceSession(
  expectedJson: string,
  replacementJson: string | null,
): Promise<boolean> {
  const expected = parse(expectedJson);
  const replacement = replacementJson === null ? null : parse(replacementJson);
  if (expected === null || (replacementJson !== null && replacement === null))
    throw new Error('Invalid authentication session.');
  if (replacement !== null && replacement.Id !== expected.Id)
    throw new Error('A token refresh cannot change the login session.');
  return locked(() => {
    const current = read();
    if (
      current === null ||
      current.Id !== expected.Id ||
      current.AccessToken !== expected.AccessToken ||
      current.RefreshToken !== expected.RefreshToken
    )
      return false;
    write(replacement);
    return true;
  });
}

export function clearSession(id: string): Promise<boolean> {
  return locked(() => {
    if (read()?.Id !== id) return false;
    write(null);
    return true;
  });
}

export function clear(): Promise<void> {
  return locked(() => write(null));
}
