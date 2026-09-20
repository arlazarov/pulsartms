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
  // Where the filled badge's edge falls, so an outlined one does not read as
  // the larger of the two.
  stopBadgeDoneRadius: 13,
  stopBadgeGap: 2,
  // Past a handful, stops close together are a corridor of them rather than
  // a pin-up at one place, and standing them in a tower says less than
  // letting them sit where they are.
  stopBadgeCluster: 6,
  // Close enough that two stops still touching are at one place: a yard and
  // its dock, several visits to one address. Coming closer will never part
  // them, so they are stood side by side rather than gathered under a count.
  stopClusterPartZoom: 16,
  stationRadius: 8,
  stationHitRadius: 10,
  // A planned stop is the only station the plan is about, so its dot is
  // drawn larger than the ones it was chosen from, inside its ring.
  recommendationDotRadius: 13,
  recommendationRadius: 19,
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
  routeFutureOpacity: 0.7,
  routeTraveledOpacity: 0.22,
  routeOutlineWidth: 2,
  routeDashArray: Object.freeze([4, 4]),
  // Empty miles read as dots, not dashes: nothing is on board.
  routeDotArray: Object.freeze([1, 3]),
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
