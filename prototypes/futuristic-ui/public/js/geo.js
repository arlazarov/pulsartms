// Map geometry helpers: an Albers conic projection for the lower 48,
// a minimal TopoJSON decoder and the Google encoded-polyline decoder.
const rad = Math.PI / 180;
const phi1 = 29.5 * rad;
const phi2 = 45.5 * rad;
const phi0 = 37.5 * rad;
const lambda0 = -96 * rad;
const n = (Math.sin(phi1) + Math.sin(phi2)) / 2;
const C = Math.cos(phi1) ** 2 + 2 * n * Math.sin(phi1);
const rho0 = Math.sqrt(C - 2 * n * Math.sin(phi0)) / n;
export const SCALE = 1300;

export function project(lng, lat) {
  const rho = Math.sqrt(C - 2 * n * Math.sin(lat * rad)) / n;
  const theta = n * (lng * rad - lambda0);
  return [
    SCALE * rho * Math.sin(theta),
    -SCALE * (rho0 - rho * Math.cos(theta)),
  ];
}

export function unproject(x, y) {
  const px = x / SCALE;
  const py = rho0 + y / SCALE;
  const rho = Math.sign(n) * Math.hypot(px, py);
  const theta = Math.atan2(px, py);
  const lat = Math.asin((C - (rho * n) ** 2) / (2 * n)) / rad;
  const lng = (lambda0 + theta / n) / rad;
  return [lng, lat];
}

function decodeArcs(topo) {
  const t = topo.transform;
  return topo.arcs.map((arc) => {
    let x = 0;
    let y = 0;
    return arc.map(([dx, dy]) => {
      x += dx;
      y += dy;
      return t
        ? [x * t.scale[0] + t.translate[0], y * t.scale[1] + t.translate[1]]
        : [dx, dy];
    });
  });
}

// Returns [{ name, rings: [[ [lng, lat], ... ], ...] }] for one object.
export function topoFeatures(topo, objectName) {
  const arcs = decodeArcs(topo);
  const ring = (idx) => {
    const pts = [];
    for (const i of idx) {
      const a = i < 0 ? arcs[~i].slice().reverse() : arcs[i];
      pts.push(...(pts.length ? a.slice(1) : a));
    }
    return pts;
  };
  const obj = topo.objects[objectName];
  const geoms = obj.type === 'GeometryCollection' ? obj.geometries : [obj];
  return geoms.map((g) => {
    const polys =
      g.type === 'Polygon' ? [g.arcs] : g.type === 'MultiPolygon' ? g.arcs : [];
    return {
      id: g.id,
      name: g.properties?.name ?? '',
      polygons: polys.map((p) => p.map(ring)),
    };
  });
}

export function pathFromRings(polygons, clip) {
  let d = '';
  for (const poly of polygons) {
    for (const r of poly) {
      if (clip && !r.some(([lng, lat]) => clip(lng, lat))) continue;
      let first = true;
      for (const [lng, lat] of r) {
        const [x, y] = project(lng, lat);
        d += `${first ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`;
        first = false;
      }
      d += 'Z';
    }
  }
  return d;
}

// Label anchor: centre of the bounding box of the largest ring.
export function labelPoint(polygons) {
  let best = null;
  let bestSize = -1;
  for (const poly of polygons) {
    const pts = poly[0].map(([lng, lat]) => project(lng, lat));
    const xs = pts.map((p) => p[0]);
    const ys = pts.map((p) => p[1]);
    const w = Math.max(...xs) - Math.min(...xs);
    const h = Math.max(...ys) - Math.min(...ys);
    if (w * h > bestSize) {
      bestSize = w * h;
      best = [
        (Math.max(...xs) + Math.min(...xs)) / 2,
        (Math.max(...ys) + Math.min(...ys)) / 2,
      ];
    }
  }
  return best;
}

export function decodePolyline(str, precision = 5) {
  const factor = 10 ** precision;
  const out = [];
  let i = 0;
  let lat = 0;
  let lng = 0;
  while (i < str.length) {
    for (const which of [0, 1]) {
      let shift = 0;
      let result = 0;
      let b;
      do {
        b = str.charCodeAt(i++) - 63;
        result |= (b & 0x1f) << shift;
        shift += 5;
      } while (b >= 0x20 && i < str.length);
      const delta = result & 1 ? ~(result >> 1) : result >> 1;
      if (which === 0) lat += delta;
      else lng += delta;
    }
    out.push([lat / factor, lng / factor]);
  }
  return out;
}
