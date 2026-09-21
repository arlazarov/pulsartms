let loading: Promise<void> | null = null;

// The provider script is loaded once for the life of the page; a failure
// clears the promise so the next mount may try again.
export function loadGoogleMaps(apiKey: string): Promise<void> {
  if (loading) return loading;
  if (!apiKey)
    return Promise.reject(new Error('Google Maps API key is missing.'));

  loading = loadWithRetry(apiKey).catch(error => {
    loading = null;
    throw error;
  });

  return loading;
}

async function loadWithRetry(apiKey: string): Promise<void> {
  let lastError: unknown;
  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      await loadScript(apiKey, attempt);
      await Promise.all([
        google.maps.importLibrary('maps'),
        google.maps.importLibrary('marker'),
      ]);
      return;
    } catch (error) {
      lastError = error;
      if (attempt < 2)
        await new Promise(resolve => setTimeout(resolve, 500 * (attempt + 1)));
    }
  }
  throw lastError;
}

function loadScript(apiKey: string, attempt: number): Promise<void> {
  // The provider hangs its callback on the window by name; nothing types
  // that, so this one place says so.
  const provider = window as unknown as Record<string, unknown> & {
    google?: { maps?: { importLibrary?: unknown } };
  };
  if (provider.google?.maps?.importLibrary) return Promise.resolve();

  return new Promise((resolve, reject) => {
    document.getElementById('google-maps-script')?.remove();
    const script = document.createElement('script');
    const callback = `initializeGoogleFleetMap_${Date.now()}_${attempt}`;
    let timeout: ReturnType<typeof setTimeout> | undefined;
    let settled = false;

    const finish = (error?: unknown) => {
      if (settled) return;
      settled = true;
      clearTimeout(timeout);
      script.onerror = null;
      delete provider[callback];
      if (error) {
        script.remove();
        reject(error);
      } else {
        resolve(undefined);
      }
    };

    provider[callback] = () => finish();
    script.id = 'google-maps-script';
    script.src =
      'https://maps.googleapis.com/maps/api/js?' +
      new URLSearchParams({
        key: apiKey,
        callback,
        loading: 'async',
        v: 'weekly',
      });
    script.async = true;
    script.onerror = () =>
      finish(new Error('Google Maps could not be loaded.'));
    timeout = setTimeout(
      () => finish(new Error('Google Maps loading timed out.')),
      10000,
    );
    document.head.appendChild(script);
  });
}
