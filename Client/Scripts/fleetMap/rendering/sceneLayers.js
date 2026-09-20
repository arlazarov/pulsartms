import { truckIcon } from './truckAppearance.js';
import { markerAnchor } from './markerAnchor.js';
import { clusterText } from './truckLabelLayout.js';
import { memoizeLast } from './layerCache.js';
import { routeLayers } from './routeAppearance.js';
import { stopAppearance, stopMarkerIcon } from './stopAppearance.js';
import {
  sceneMetrics as metrics,
  createLabelFonts,
  labelSubLayers,
} from './sceneMetrics.js';
import { defaultStopLabelStyle } from './stopLabelStyle.js';
import { stopCardLayers } from './stopCardLayers.js';
const emptyClusters = Object.freeze([]);

// One cache per scene. Zoom-driven truck groups do not rebuild roads or stations.
export function createSceneLayers({
  ScatterplotLayer,
  PathLayer,
  IconLayer,
  TextLayer,
  routeDashExtensions,
}) {
  const stationLayer = memoizeLast(),
    distanceLabels = memoizeLast();
  const stopLayers = new Map();
  const stopGroup = memoizeLast();
  const vehicleLayers = memoizeLast();
  const clusterLabels = memoizeLast();
  const recommendationLayers = memoizeLast();
  const fuelVisitLabels = memoizeLast();
  const fuelEditingLayers = memoizeLast();
  const labelFonts = memoizeLast();
  const stationPoints = (id, data, visible, onHover, onClick) =>
    new ScatterplotLayer({
      id,
      data,
      visible,
      pickable: true,
      getPosition: d => d.position,
      radiusUnits: 'pixels',
      getRadius: metrics.stationRadius,
      stroked: true,
      lineWidthUnits: 'pixels',
      getLineWidth: d => (d.selected ? 3 : 2),
      getFillColor: d => d.color,
      getLineColor: d =>
        d.selected || d.recommended ? [49, 94, 234] : [255, 255, 255],
      autoHighlight: true,
      highlightColor: [49, 94, 234, 100],
      onHover,
      onClick,
      parameters: { depthCompare: 'always' },
    });
  return ({
    lines,
    stationData,
    stationsVisible,
    stopData,
    distanceData,
    vehicles,
    clusters = emptyClusters,
    hasSelectedTruck = false,
    selectCluster,
    hoveredTruck,
    setHover,
    selectStop,
    hoverTruck,
    selectTruck,
    selectStation,
    pixelRatio = 1,
    stopLabelStyle = defaultStopLabelStyle,
  }) => {
    const fonts = labelFonts([pixelRatio, stopLabelStyle.size], () =>
      createLabelFonts(pixelRatio, stopLabelStyle.size),
    );
    const roads = [...lines].filter(line => line.map && line.path.length > 1);
    const hasSelectedNextRoute = roads.some(
      line =>
        line.visible !== false &&
        line.routeSelected === true &&
        (line.routeRole === 'future' || line.routeRole === 'deadhead'),
    );
    const layers = roads
      .sort((a, b) => (a.zIndex ?? 0) - (b.zIndex ?? 0))
      .flatMap(line =>
        routeLayers(
          line,
          PathLayer,
          routeDashExtensions,
          hasSelectedNextRoute && line.routeSelected !== true,
        ),
      );
    // All station fills cover roads; recommendations, stops and trucks retain priority.
    const truckLayers = [...layers];
    truckLayers.push(
      stationLayer(
        [stationData, stationsVisible, setHover, selectStation],
        () =>
          stationPoints(
            'fuel-points',
            stationData.filter(d => !d.recommended),
            stationsVisible,
            setHover,
            selectStation,
          ),
      ),
    );
    truckLayers.push(
      ...recommendationLayers(
        [stationData, stationsVisible, setHover, selectStation],
        () => {
          const data = stationData.filter(d => d.recommended && !d.editing);
          return [
            stationPoints(
              'fuel-recommendation-points',
              data,
              true,
              setHover,
              selectStation,
            ),
            new ScatterplotLayer({
              id: 'fuel-recommendation-rings',
              data,
              visible: true,
              getPosition: d => d.position,
              getRadius: metrics.recommendationRadius,
              radiusUnits: 'pixels',
              filled: false,
              stroked: true,
              getLineColor: [49, 94, 234],
              getLineWidth: 3,
              lineWidthUnits: 'pixels',
              pickable: true,
              onHover: setHover,
              onClick: selectStation,
              parameters: { depthCompare: 'always' },
            }),
          ];
        },
      ),
    );
    truckLayers.push(
      fuelVisitLabels(
        [
          stationData,
          stationsVisible,
          setHover,
          selectStation,
          fonts.fuelVisit,
        ],
        () =>
          new TextLayer({
            id: 'fuel-recommendation-numbers',
            data: stationData.filter(
              d =>
                d.recommended &&
                !d.editing &&
                typeof d.numbers === 'string' &&
                d.numbers.trim(),
            ),
            visible: true,
            characterSet: 'Fuel 0123456789/',
            getPosition: d => d.position,
            getText: d => `Fuel ${d.numbers}`,
            getSize: metrics.fuelVisitLabelSize,
            sizeUnits: 'pixels',
            getPixelOffset: [0, -metrics.fuelVisitLabelOffset],
            getTextAnchor: 'middle',
            getAlignmentBaseline: 'center',
            getColor: [255, 255, 255],
            background: true,
            getBackgroundColor: [30, 41, 59],
            backgroundPadding: metrics.fuelVisitLabelPadding,
            backgroundBorderRadius: 4,
            getBorderColor: [255, 255, 255, 220],
            getBorderWidth: 1,
            fontFamily: 'Arial, sans-serif',
            fontSettings: fonts.fuelVisit,
            _subLayerProps: labelSubLayers,
            fontWeight: 'bold',
            billboard: true,
            pickable: true,
            onHover: setHover,
            onClick: selectStation,
            parameters: { depthCompare: 'always' },
          }),
      ),
    );
    truckLayers.push(
      ...fuelEditingLayers(
        [
          stationData,
          stationsVisible,
          setHover,
          selectStation,
          fonts.fuelVisit,
        ],
        () => {
          const data = stationData.filter(d => d.editing);
          return [
            stationPoints(
              'fuel-editing-points',
              data,
              true,
              setHover,
              selectStation,
            ),
            new ScatterplotLayer({
              id: 'fuel-editing-ring',
              data,
              getPosition: d => d.position,
              getRadius: metrics.fuelEditingRadius,
              radiusUnits: 'pixels',
              filled: false,
              stroked: true,
              getLineColor: [49, 94, 234],
              getLineWidth: 3,
              lineWidthUnits: 'pixels',
              pickable: true,
              onHover: setHover,
              onClick: selectStation,
              parameters: { depthCompare: 'always' },
            }),
            new TextLayer({
              id: 'fuel-editing-label',
              data,
              characterSet: 'Editing',
              getPosition: d => d.position,
              getText: () => 'Editing',
              getSize: metrics.fuelVisitLabelSize,
              sizeUnits: 'pixels',
              getPixelOffset: [0, -metrics.fuelEditingLabelOffset],
              getTextAnchor: 'middle',
              getAlignmentBaseline: 'center',
              getColor: [255, 255, 255],
              background: true,
              getBackgroundColor: [49, 94, 234],
              backgroundPadding: metrics.fuelVisitLabelPadding,
              backgroundBorderRadius: 4,
              getBorderColor: [255, 255, 255, 220],
              getBorderWidth: 1,
              fontFamily: 'Arial, sans-serif',
              fontSettings: fonts.fuelVisit,
              _subLayerProps: labelSubLayers,
              fontWeight: 'bold',
              billboard: true,
              pickable: true,
              onHover: setHover,
              onClick: selectStation,
              parameters: { depthCompare: 'always' },
            }),
          ];
        },
      ),
    );
    // On the cluster's own point, and beneath the stops.
    //
    // It stays put: offsetting it to escape what it covered only trailed a
    // leader line halfway across a state as the map zoomed out. What it used
    // to cover was a stop of the route being read, so the stops are drawn
    // over it and truck labels step around it instead.
    truckLayers.push(
      clusterLabels(
        [clusters, selectCluster, setHover, fonts],
        () =>
          new TextLayer({
            id: 'truck-clusters',
            data: clusters,
            characterSet: 'auto',
            getPosition: d => d.position,
            getText: clusterText,
            getSize: metrics.truckLabelSize,
            sizeUnits: 'pixels',
            getColor: [255, 255, 255],
            background: true,
            getBackgroundColor: [30, 41, 59],
            backgroundPadding: metrics.truckClusterPadding,
            backgroundBorderRadius: metrics.truckClusterBadge,
            getBorderColor: [255, 255, 255],
            getBorderWidth: 2,
            fontFamily: 'Arial, sans-serif',
            fontSettings: fonts.truck,
            _subLayerProps: labelSubLayers,
            fontWeight: 'bold',
            billboard: true,
            pickable: true,
            onHover: setHover,
            onClick: selectCluster,
            parameters: { depthCompare: 'always' },
          }),
      ),
    );
    // Keep each geographic anchor and badge together when selection changes priority.
    truckLayers.push(
      ...stopGroup([stopData, setHover, selectStop, fonts], () => {
        const activeStops = new Set(stopData.map(stop => stop.id));
        for (const id of stopLayers.keys())
          if (!activeStops.has(id)) stopLayers.delete(id);
        return stopData.flatMap(stop => {
          const id = `route-stop-${stop.id}`;
          const cached = stopLayers.get(stop.id);
          if (
            cached &&
            cached.fonts === fonts &&
            cached.setHover === setHover &&
            cached.selectStop === selectStop &&
            [
              'position',
              'number',
              'job',
              'color',
              'onSelect',
              'onHover',
              'markerLabel',
              'markerOffsetX',
              'markerOffsetY',
            ].every(field => cached.stop[field] === stop[field])
          )
            return cached.layers;
          const data = [stop];
          const appearance = stopAppearance(stop.job, stop.color);
          const { url: iconAtlas, ...circle } = stopMarkerIcon(appearance.fill);
          const layers = [
            ...(stop.markerOffsetX || stop.markerOffsetY
              ? [
                  new ScatterplotLayer({
                    id: `${id}-anchor`,
                    data,
                    getPosition: s => s.position,
                    getRadius: metrics.stopRadius,
                    radiusUnits: 'pixels',
                    getFillColor: appearance.fill,
                    opacity: 1,
                    stroked: true,
                    getLineColor: appearance.border,
                    getLineWidth: 1,
                    lineWidthUnits: 'pixels',
                    pickable: true,
                    onHover: setHover,
                    onClick: selectStop,
                    parameters: { depthCompare: 'always' },
                  }),
                ]
              : []),
            new IconLayer({
              id: `${id}-points`,
              data,
              getPosition: s => s.position,
              iconAtlas,
              iconMapping: { circle: { ...circle, x: 0, y: 0 } },
              // Deck resolves packed frames through an accessor, not a constant attribute.
              getIcon: () => 'circle',
              getSize: metrics.stopBadgeDiameter,
              sizeUnits: 'pixels',
              getPixelOffset: s => [s.markerOffsetX, s.markerOffsetY],
              billboard: true,
              pickable: true,
              onHover: setHover,
              onClick: selectStop,
              parameters: { depthCompare: 'always' },
            }),
            new TextLayer({
              id: `${id}-numbers`,
              characterSet: '0123456789/',
              data,
              getPosition: s => s.position,
              getText: s => s.markerLabel,
              getPixelOffset: s => [s.markerOffsetX, s.markerOffsetY],
              getTextAnchor: 'middle',
              getAlignmentBaseline: 'center',
              getSize: metrics.numberSize,
              sizeUnits: 'pixels',
              getColor: appearance.text,
              background: false,
              fontFamily: 'Arial, sans-serif',
              fontSettings: fonts.stop,
              _subLayerProps: labelSubLayers,
              fontWeight: 'bold',
              billboard: true,
              pickable: true,
              onHover: setHover,
              onClick: selectStop,
              parameters: { depthCompare: 'always' },
            }),
          ];
          stopLayers.set(stop.id, {
            stop,
            setHover,
            selectStop,
            fonts,
            layers,
          });
          return layers;
        });
      }),
    );
    const distanceLayer = distanceLabels(
      [distanceData, fonts, stopLabelStyle],
      () => stopCardLayers(TextLayer, distanceData, stopLabelStyle, fonts),
    );
    // Text carries its own alpha per row, so the unit numbers fade with the
    // arrows they belong to rather than needing a layer of their own.
    const quietAlpha = (truck, full) =>
      hasSelectedTruck && !truck.selected
        ? Math.round(full * metrics.truckMutedOpacity)
        : full;
    truckLayers.push(
      ...vehicleLayers(
        [vehicles, hoveredTruck, fonts, hasSelectedTruck],
        () => [
          new IconLayer({
            id: 'truck-label-anchors',
            data: vehicles.filter(
              t =>
                t.labelOffset &&
                (t.labelOffset[0] !== 0 ||
                  t.labelOffset[1] !== -metrics.truckLabelOffset),
            ),
            getPosition: t => t.position,
            getIcon: t => markerAnchor(t.labelOffset),
            getSize: t => markerAnchor(t.labelOffset).size,
            sizeUnits: 'pixels',
            billboard: true,
            pickable: true,
            onHover: hoverTruck,
            onClick: selectTruck,
            parameters: { depthCompare: 'always' },
          }),
          // Two icon layers, not one: the arrows are drawn images, so only
          // a whole layer can be faded. With a truck chosen the rest of the
          // fleet steps back instead of competing with its route.
          ...[false, true].map(
            quiet =>
              new IconLayer({
                id: quiet ? 'truck-icons-quiet' : 'truck-icons',
                data: vehicles.filter(
                  t => (hasSelectedTruck && !t.selected) === quiet,
                ),
                opacity: quiet ? metrics.truckMutedOpacity : 1,
                getPosition: t => t.position,
                getIcon: t => truckIcon(t.engine, t.speed),
                getSize: t =>
                  (quiet ? metrics.truckSecondarySize : metrics.truckSize) *
                  (t.unit === hoveredTruck ? metrics.truckHoverScale : 1),
                sizeUnits: 'pixels',
                billboard: true,
                getAngle: t => -t.heading,
                updateTriggers: { getSize: [hoveredTruck, hasSelectedTruck] },
                pickable: true,
                onHover: hoverTruck,
                onClick: selectTruck,
                parameters: { depthCompare: 'always' },
              }),
          ),
          new TextLayer({
            id: 'truck-numbers',
            characterSet: 'auto',
            data: vehicles,
            getPosition: t => t.position,
            getText: t => t.unit,
            getSize: metrics.truckLabelSize,
            sizeUnits: 'pixels',
            getColor: t => [255, 255, 255, quietAlpha(t, 255)],
            getPixelOffset: t =>
              t.labelOffset ?? [0, -metrics.truckLabelOffset],
            background: true,
            getBackgroundColor: t => [
              ...(t.selected || t.unit === hoveredTruck
                ? [49, 94, 234]
                : [30, 41, 59]),
              quietAlpha(t, 255),
            ],
            backgroundPadding: metrics.truckLabelPadding,
            backgroundBorderRadius: 5,
            getBorderColor: t => [255, 255, 255, quietAlpha(t, 220)],
            getBorderWidth: 1,
            updateTriggers: {
              getColor: hasSelectedTruck,
              getBorderColor: hasSelectedTruck,
              getBackgroundColor: [hoveredTruck, hasSelectedTruck],
            },
            fontFamily: 'Arial, sans-serif',
            fontSettings: fonts.truck,
            _subLayerProps: labelSubLayers,
            fontWeight: 'bold',
            billboard: true,
            pickable: true,
            onHover: hoverTruck,
            onClick: selectTruck,
            parameters: { depthCompare: 'always' },
          }),
        ],
      ),
    );
    truckLayers.push(...distanceLayer);
    return truckLayers.filter(
      layer => layer.props.visible !== false && layer.props.data?.length > 0,
    );
  };
}
