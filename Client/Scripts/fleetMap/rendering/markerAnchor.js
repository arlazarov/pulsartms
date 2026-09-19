const icons = new Map();

export function markerAnchor(pixelOffset) {
  const [dx, dy] = pixelOffset;
  const key = `${dx}:${dy}`;
  if (!icons.has(key)) {
    const left = Math.min(0, dx) - 6,
      top = Math.min(0, dy) - 6;
    const width = Math.abs(dx) + 12,
      height = Math.abs(dy) + 12;
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${width * 4}" height="${height * 4}" viewBox="${left} ${top} ${width} ${height}">
<path d="M0 0 L${dx} ${dy}" stroke="white" stroke-width="4"/>
<path d="M0 0 L${dx} ${dy}" stroke="#1e293b" stroke-width="2"/>
<circle cx="0" cy="0" r="5" fill="#1e293b" stroke="white" stroke-width="2"/>
</svg>`;
    icons.set(key, {
      url: `data:image/svg+xml,${encodeURIComponent(svg)}`,
      width: width * 4,
      height: height * 4,
      anchorX: -left * 4,
      anchorY: -top * 4,
      mask: false,
      size: height,
    });
  }
  return icons.get(key);
}
