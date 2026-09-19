// CSS-pixel dimensions; the overlay handles device-pixel density separately.
export const sceneMetrics = Object.freeze({
  labelSize: 16,
  stopLabelSize: 14,
  numberSize: 15,
  truckLabelSize: 13,
  truckLabelPadding: [9, 4],
  truckClusterPadding: [12, 10],
  truckClusterBadge: 12,
  stopRadius: 3,
  stopBadgeOffset: 0,
  stopBadgeDiameter: 34,
  stopBadgeGap: 2,
  stationRadius: 8,
  stationHitRadius: 10,
  recommendationRadius: 12,
  fuelEditingRadius: 14,
  fuelEditingLabelOffset: 25,
  fuelVisitLabelSize: 12,
  fuelVisitLabelOffset: 21,
  fuelVisitLabelPadding: [6, 4],
  labelOffset: 34,
  truckSize: 28,
  truckSecondarySize: 23,
  truckClusterRadius: 64,
  truckClusterMaxZoom: 12,
  truckHoverScale: 1.1,
  truckLabelOffset: 30,
  routeWidthScale: 1.75,
  routeMinWidth: 5,
  routeSecondaryWidth: 3,
  routeCurrentMinWidth: 5,
  routeCurrentWidthScale: 1.25,
  routeMutedOpacity: 0.4,
  routeTraveledOpacity: 0.22,
  routeOutlineWidth: 2,
  routeDashArray: Object.freeze([4, 4]),
});

// Rasterize at the displayed physical font size so small glyphs retain hinting.
export function createLabelFonts(
  pixelRatio = 1,
  stopLabelSize = sceneMetrics.stopLabelSize,
) {
  const density =
    Number.isFinite(pixelRatio) && pixelRatio > 0 ? pixelRatio : 1;
  const settings = size => ({
    sdf: false,
    fontSize: Math.max(1, Math.round(size * density)),
  });
  return {
    label: settings(sceneMetrics.labelSize),
    stopLabel: settings(stopLabelSize),
    stop: settings(sceneMetrics.numberSize),
    truck: settings(sceneMetrics.truckLabelSize),
    fuelVisit: settings(sceneMetrics.fuelVisitLabelSize),
  };
}

// Keep bilinear edge antialiasing, but do not blend adjacent atlas mip levels.
export const labelSubLayers = Object.freeze({
  characters: {
    textureParameters: {
      minFilter: 'linear',
      magFilter: 'linear',
      mipmapFilter: 'nearest',
      addressModeU: 'clamp-to-edge',
      addressModeV: 'clamp-to-edge',
    },
  },
});
