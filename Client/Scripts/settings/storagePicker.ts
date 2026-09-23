// Google's folder picker for connecting a company's Drive. The server hands
// a short-lived access token for this one connection; the administrator
// picks a folder in My Drive or a shared drive, and only its id goes back to
// the server, which checks it before using it. Picking the folder is what
// lets PulsR's narrow Drive access reach it.

interface PickerSession {
  accessToken: string;
  clientId: string;
  apiKey: string;
  appId: string;
}

interface PickedDocument {
  id: string;
  mimeType?: string;
}

interface PickerResult {
  action: string;
  docs?: PickedDocument[];
}

interface PickerBuilder {
  addView(view: unknown): PickerBuilder;
  enableFeature(feature: unknown): PickerBuilder;
  setOAuthToken(token: string): PickerBuilder;
  setDeveloperKey(key: string): PickerBuilder;
  setAppId(id: string): PickerBuilder;
  setTitle(title: string): PickerBuilder;
  setCallback(callback: (result: PickerResult) => void): PickerBuilder;
  build(): { setVisible(visible: boolean): void; dispose(): void };
}

interface GooglePicker {
  PickerBuilder: new () => PickerBuilder;
  DocsView: new (view: unknown) => {
    setIncludeFolders(on: boolean): unknown;
    setSelectFolderEnabled(on: boolean): unknown;
    setEnableDrives(on: boolean): unknown;
    setMimeTypes(types: string): unknown;
  };
  ViewId: { FOLDERS: unknown };
  Feature: { SUPPORT_DRIVES: unknown };
  Action: { PICKED: string; CANCEL: string };
}

declare global {
  interface Window {
    gapi?: { load(name: string, done: () => void): void };
    google?: { picker?: GooglePicker };
  }
}

const folder = 'application/vnd.google-apps.folder';
let loading: Promise<GooglePicker> | null = null;

function load(): Promise<GooglePicker> {
  loading ??= new Promise<GooglePicker>((resolve, reject) => {
    const ready = () =>
      window.gapi!.load('picker', () =>
        window.google?.picker
          ? resolve(window.google.picker)
          : reject(new Error('The folder picker did not load.')),
      );
    if (window.gapi) {
      ready();
      return;
    }
    const script = document.createElement('script');
    script.src = 'https://apis.google.com/js/api.js';
    script.async = true;
    script.onload = ready;
    script.onerror = () => {
      loading = null;
      reject(new Error('The folder picker did not load.'));
    };
    document.head.append(script);
  });
  return loading;
}

// The picked folder's id, or null when the administrator closed the picker.
export async function pickFolder(
  session: PickerSession,
): Promise<string | null> {
  const picker = await load();
  const view = new picker.DocsView(picker.ViewId.FOLDERS);
  view.setIncludeFolders(true);
  view.setSelectFolderEnabled(true);
  view.setEnableDrives(true);
  view.setMimeTypes(folder);
  return new Promise<string | null>(resolve => {
    const dialog = new picker.PickerBuilder()
      .addView(view)
      .enableFeature(picker.Feature.SUPPORT_DRIVES)
      .setOAuthToken(session.accessToken)
      .setDeveloperKey(session.apiKey)
      .setAppId(session.appId)
      .setTitle('Choose the folder for PulsR files')
      .setCallback(result => {
        if (result.action === picker.Action.PICKED) {
          const chosen = result.docs?.find(doc => doc.mimeType === folder);
          dialog.dispose();
          resolve(chosen?.id ?? null);
        } else if (result.action === picker.Action.CANCEL) {
          dialog.dispose();
          resolve(null);
        }
      })
      .build();
    dialog.setVisible(true);
  });
}
