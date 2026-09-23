// One messaging stream per browser and account, not one per tab or page.
// Every tab that shows messages joins with its account's scope; a Web Lock
// elects one tab per scope to read the server's stream, and it passes each
// signal to the others over a BroadcastChannel of the same scope. When the
// leading tab closes, the lock passes to another. Without both APIs there
// is no election: every tab leads and reads its own stream, since a leader
// that cannot relay would leave the other tabs deaf. Signals carry no
// content: "conversation X changed", "read everything again" after a
// reconnect, and a slow "poll" tick while the stream is down.

interface DotNetRef {
  invokeMethodAsync(name: string, ...args: unknown[]): Promise<unknown>;
}

type Signal =
  | { kind: 'change'; id: string }
  | { kind: 'resync' }
  | { kind: 'poll' };

const channelName = 'pulsr-messaging';
const lockName = 'pulsr-messaging-stream';
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const scopePattern = /^[0-9a-z:-]{1,128}$/i;

let dotnet: DotNetRef | null = null;
let channel: BroadcastChannel | null = null;
let release: (() => void) | null = null;
// Changes on every join and leave. The same .NET reference joins again
// after a sign-in change, so a lock granted for an earlier join is
// recognised by its generation, not by the reference.
let generation = 0;

function valid(value: unknown): value is Signal {
  const signal = value as Partial<{ kind: string; id: string }> | null;
  if (signal === null || typeof signal !== 'object') return false;
  if (signal.kind === 'resync' || signal.kind === 'poll') return true;
  return (
    signal.kind === 'change' &&
    typeof signal.id === 'string' &&
    guid.test(signal.id)
  );
}

function deliver(value: unknown): void {
  if (!dotnet || !valid(value)) return;
  void dotnet.invokeMethodAsync(
    'Receive',
    value.kind,
    value.kind === 'change' ? value.id : null,
  );
}

// Scope names the signed-in account and session, so a tab signed in as
// someone else never shares a leader or hears another account's signals.
export function join(ref: DotNetRef, scope: string): void {
  leave();
  dotnet = ref;
  const joined = generation;
  const locks = globalThis.navigator?.locks;
  if (
    typeof BroadcastChannel !== 'function' ||
    !locks?.request ||
    typeof scope !== 'string' ||
    !scopePattern.test(scope)
  ) {
    void ref.invokeMethodAsync('Lead');
    return;
  }
  channel = new BroadcastChannel(`${channelName}:${scope}`);
  channel.onmessage = event => deliver(event.data);
  void locks.request(
    `${lockName}:${scope}`,
    () =>
      new Promise<void>(resolve => {
        // A join that has since ended gives the lock straight back.
        if (generation !== joined) {
          resolve();
          return;
        }
        release = resolve;
        void ref.invokeMethodAsync('Lead');
      }),
  );
}

// From the leading tab: to every other tab, and to this one.
export function post(signal: unknown): void {
  if (!valid(signal)) return;
  channel?.postMessage(signal);
  deliver(signal);
}

export function leave(): void {
  generation++;
  release?.();
  release = null;
  channel?.close();
  channel = null;
  dotnet = null;
}
