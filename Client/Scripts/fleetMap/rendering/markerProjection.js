export function markerProjection(zoom) {
  const world = 256 * 2 ** (Number.isFinite(zoom) ? zoom : 12);
  return ([lng, lat]) => {
    const sin = Math.sin((Math.max(-85, Math.min(85, lat)) * Math.PI) / 180);
    return [
      ((lng + 180) / 360) * world,
      (0.5 - Math.log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * world,
    ];
  };
}
