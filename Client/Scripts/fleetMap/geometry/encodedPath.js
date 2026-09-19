// @ts-check

const scale = 1e6;

/**
 * Route geometry arrives as one encoded string per leg rather than an object
 * per point. It is decoded here, once, where the payload enters the map, so
 * nothing past this point knows there are two forms.
 * @param {string | null | undefined} text
 * @returns {import('../contracts.d.ts').RoutePoint[]}
 */
export function decodePath(text) {
  /** @type {import('../contracts.d.ts').RoutePoint[]} */
  const points = [];
  if (!text) return points;
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
 * @param {Uint8Array} bytes
 */
export function parseMapPayload(bytes) {
  return JSON.parse(new TextDecoder().decode(bytes), (_key, value) =>
    value &&
    typeof value.path === 'string' &&
    Array.isArray(value.points) &&
    value.points.length === 0
      ? { ...value, points: decodePath(value.path), path: undefined }
      : value,
  );
}
