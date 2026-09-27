export function applyTheme(theme: string): void {
  document.documentElement.dataset.theme = theme === 'dark' ? 'dark' : 'light';
}

// The interface choice is kept on this device, per account, until an
// account preference exists for it. The current interface is the default
// and what a missing, unreadable or unknown value falls back to.
const interfaceKey = (account: string) => `pulsr.interface.${account}`;

export function readInterface(account: string): string {
  try {
    return localStorage.getItem(interfaceKey(account)) === 'futuristic'
      ? 'futuristic'
      : 'current';
  } catch {
    return 'current';
  }
}

export function saveInterface(account: string, value: string): boolean {
  try {
    localStorage.setItem(
      interfaceKey(account),
      value === 'futuristic' ? 'futuristic' : 'current',
    );
    return true;
  } catch {
    return false;
  }
}

export function applyInterface(value: string): void {
  if (value === 'futuristic')
    document.documentElement.dataset.interface = 'futuristic';
  else delete document.documentElement.dataset.interface;
}
