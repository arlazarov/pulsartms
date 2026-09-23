// Browser notifications for new driver messages, only after the dispatcher
// asks for them. The leading tab calls notify once per browser; a shared
// tag makes a later notice replace an earlier one rather than pile up. A
// notice names no driver and quotes nothing, since it may show on a locked
// screen, and it is skipped while this tab is in front, where the
// navigation count already shows.

type Permission = 'unsupported' | 'default' | 'granted' | 'denied';

const preferenceKey = 'pulsr-messaging-notifications';
const tag = 'pulsr-messages';

function api(): typeof Notification | null {
  return typeof globalThis.Notification === 'function'
    ? globalThis.Notification
    : null;
}

export function permission(): Permission {
  return api()?.permission ?? 'unsupported';
}

export async function request(): Promise<Permission> {
  const notification = api();
  if (!notification) return 'unsupported';
  const answer = await notification.requestPermission();
  if (answer === 'granted') setEnabled(true);
  return answer;
}

// The dispatcher's choice in this browser; off until turned on.
export function enabled(): boolean {
  try {
    return globalThis.localStorage?.getItem(preferenceKey) === 'on';
  } catch {
    return false;
  }
}

export function setEnabled(on: boolean): void {
  try {
    globalThis.localStorage?.setItem(preferenceKey, on ? 'on' : 'off');
  } catch {
    // Storage refused: the choice lasts for this page only.
  }
}

export function notify(count: number, more: boolean): boolean {
  const notification = api();
  if (
    !notification ||
    notification.permission !== 'granted' ||
    !enabled() ||
    globalThis.document?.visibilityState === 'visible' ||
    !Number.isInteger(count) ||
    count < 1
  )
    return false;
  const shown = new notification('New driver message', {
    body: `${count}${more ? '+' : ''} conversation${
      count === 1 && !more ? '' : 's'
    } with unread messages`,
    tag,
  });
  shown.onclick = () => {
    globalThis.focus?.();
    globalThis.location?.assign('/messages');
    shown.close();
  };
  return true;
}
