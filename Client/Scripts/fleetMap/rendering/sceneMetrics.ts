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
  // The same badge with the truck's ring around it.
  stopBadgeStandingDiameter: 46,
  // And with a wider white rim, for a badge drawn over a truck it is near
  // but not at.
  stopBadgeStackedDiameter: 38,
  // The edge a badge wears while its load is the one being looked at: the
  // dark of the map's labels, which reads against every route colour.
  stopBadgePickedEdge: [30, 41, 59, 255],
  // How much of a truck must show from behind a badge for the crescent to
  // read as a truck. Less than this and the badge wears the truck as a ring
  // instead: a white rim with nothing behind it says the stop is alone.
  truckCrescent: 6,
  // Where the filled badge's edge falls, so an outlined one does not read as
  // the larger of the two.
  stopBadgeDoneRadius: 13,
  stopBadgeGap: 2,
  stationRadius: 8,
  stationHitRadius: 10,
  // A planned stop is the only station the plan is about, so its dot is
  // drawn larger than the ones it was chosen from, inside its ring - but a
  // step larger, not the largest thing on the map. At 13 inside 19 the ring
  // was wider than a stop's badge, and a fuel stop is not more than a stop.
  recommendationDotRadius: 10,
  recommendationRadius: 14,
  fuelEditingRadius: 14,
  fuelEditingLabelOffset: 25,
  fuelVisitLabelSize: 12,
  // Clear of the ring it names: its radius, half the label's own height,
  // and a gap. At twenty-one the label sat on the marker.
  fuelVisitLabelOffset: 27,
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
// The font settings each kind of label is drawn with, at this screen's
// pixel density.
export type LabelFonts = {
  label: FontSettings;
  stopLabel: FontSettings;
  stop: FontSettings;
  truck: FontSettings;
  fuelVisit: FontSettings;
};
type FontSettings = { sdf: boolean; fontSize: number };

export function createLabelFonts(
  pixelRatio = 1,
  stopLabelSize: number = sceneMetrics.stopLabelSize,
) {
  const density =
    Number.isFinite(pixelRatio) && pixelRatio > 0 ? pixelRatio : 1;
  const settings = (size: number) => ({
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
