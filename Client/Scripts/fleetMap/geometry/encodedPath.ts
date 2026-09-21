import type {
  MapPlan,
  NextLoad,
  NextLoadsPayload,
  RoutePayload,
  RoutePoint,
} from '../contracts.d.ts';

const scale = 1e6;

/**
 * Route geometry arrives as one encoded string per leg rather than an object
 * per point. It is decoded here, once, where the payload enters the map, so
 * nothing past this point knows there are two forms.
 */
export function decodePath(encoded: string | null | undefined): RoutePoint[] {
  const points: RoutePoint[] = [];
  if (!encoded) return points;
  const text = encoded;
  let index = 0,
    latitude = 0,
    longitude = 0;
  // Six decimal places of longitude need more than the 32 bits the bitwise
  // operators keep, so the chunks are assembled with arithmetic.
  function read() {
    let bits = 0,
      factor = 1,
      chunk;
    do {
      if (index >= text.length) throw new Error('Encoded path is cut short.');
      chunk = text.charCodeAt(index++) - 63;
      if (chunk < 0 || chunk > 63) throw new Error('Encoded path is invalid.');
      bits += (chunk & 0x1f) * factor;
      factor *= 32;
    } while (chunk >= 0x20);
    return bits % 2 === 1 ? -(bits + 1) / 2 : bits / 2;
  }
  while (index < text.length) {
    latitude += read();
    longitude += read();
    points.push({ latitude: latitude / scale, longitude: longitude / scale });
  }
  return points;
}

/**
 * JSON.parse for anything sent to the map. A leg that carries `path` and no
 * points gets its points back.
 */
export function parseMapPayload(
  bytes: Uint8Array,
): RoutePayload | MapPlan | NextLoadsPayload | NextLoad[] {
  return JSON.parse(new TextDecoder().decode(bytes), (_key, value) =>
    value &&
    typeof value.path === 'string' &&
    Array.isArray(value.points) &&
    value.points.length === 0
      ? { ...value, points: decodePath(value.path), path: undefined }
      : value,
  );
}

// Each port names the payload it carries, so no caller has to say it twice.
export function parseRouteEditorPayload(
  bytes: Uint8Array | null | undefined,
): MapPlan | null {
  return bytes ? (parseMapPayload(bytes) as MapPlan) : null;
}

export function parseRoutePayload(bytes: Uint8Array): RoutePayload {
  return parseMapPayload(bytes) as RoutePayload;
}

export function parseNextLoads(
  bytes: Uint8Array,
): NextLoadsPayload | NextLoad[] {
  return parseMapPayload(bytes) as NextLoadsPayload | NextLoad[];
}
