import type { DeckLayer } from './deckLayer.ts';
import type { StopLabelStyle } from './stopLabelStyle.ts';
import { pickNearbyStation } from './stationTouch.ts';
import { readStopLabelStyle } from './stopLabelStyle.ts';
import { createMapRepaint } from '../provider/mapRepaint.ts';

// Until the provider says the camera is synchronised, nothing is drawn:
// layers put up before then stand where the camera used to be.
const hideUnsynchronizedLayers = () => false;

type Pick = { object?: any };

/**
 * The canvas the scene is drawn on, and what the screen says about it.
 *
 * All fleet layers share one foreground canvas with an explicit drawing
 * order; on vector maps it receives the provider's own camera transformer.
 * This module owns everything that is true of the screen rather than of the
 * map's contents - when the camera is ready, how dense the display is, what
 * the page's theme makes a stop's label look like - and asks for a redraw
 * whenever one of them changes.
 */
export function createSceneOverlay(
  map: google.maps.Map,
  GoogleMapsOverlay: new (props: Record<string, any>) => any,
  {
    onChange,
    canPick,
    onPick,
  }: {
    onChange: () => void;
    // Whether a click on nothing should look for a station near it.
    canPick: () => boolean;
    onPick: (nearby: Pick) => void;
  },
) {
  const element = map.getDiv();
  const viewport = element.ownerDocument?.defaultView;
  const repaint = createMapRepaint(map);
  let disposed = false;
  let cameraReady = false;
  let pixelRatio = viewport?.devicePixelRatio || 1;
  let stopLabelStyle: StopLabelStyle = readStopLabelStyle(element);

  const overlay = new GoogleMapsOverlay({
    id: 'fleet-top-layer',
    interleaved: false,
    useDevicePixels: true,
    layerFilter: cameraReady ? null : hideUnsynchronizedLayers,
    onLoad: () => {
      if (disposed) return;
      cameraReady = false;
      overlay.setProps({ layerFilter: hideUnsynchronizedLayers });
      repaint.request(() => {
        cameraReady = true;
        onChange();
      });
    },
    style: {
      top: '0',
      left: '0',
      width: '100%',
      height: '100%',
      zIndex: '1',
      pointerEvents: 'none',
    },
    layers: [],
    onClick: (info: Pick, event: Record<string, any> | undefined) => {
      if (disposed || !canPick()) return;
      const nearby = pickNearbyStation(overlay, info, event);
      if (nearby?.object) onPick(nearby);
    },
  });
  // The provider owns camera sync; grouping and label spacing depend on zoom.
  overlay.setMap(map);

  const densityChanged = () => {
    const next = viewport!.devicePixelRatio || 1;
    if (next === pixelRatio) return;
    pixelRatio = next;
    onChange();
  };
  viewport?.addEventListener('resize', densityChanged);

  // A stop's label is styled by the page, so the page's theme changes it.
  const themeObserver = viewport?.MutationObserver
    ? new viewport.MutationObserver(() => {
        stopLabelStyle = readStopLabelStyle(element);
        onChange();
      })
    : null;
  for (const node of [
    element.ownerDocument?.documentElement,
    element.ownerDocument?.body,
  ])
    if (node)
      themeObserver?.observe(node, {
        attributes: true,
        attributeFilter: ['data-theme', 'class', 'style'],
      });

  element.dataset.renderer = 'gpu';
  const modeListener = map.addListener('renderingtype_changed', () => {
    const mode = map.getRenderingType();
    element.dataset.renderer = `gpu-${mode}`;
    if (mode !== 'VECTOR')
      console.warn(`[Fleet map] Vector renderer unavailable: ${mode}`);
  });

  return {
    pixelRatio: () => pixelRatio,
    stopLabelStyle: () => stopLabelStyle,
    draw(layers: DeckLayer[]) {
      overlay.setProps({
        layerFilter: cameraReady ? null : hideUnsynchronizedLayers,
        layers,
      });
    },
    // Each step runs even if an earlier one throws: the overlay holds a
    // WebGL context and the observers hold the whole scene.
    release: [
      () => {
        disposed = true;
      },
      () => modeListener.remove(),
      () => themeObserver?.disconnect(),
      () => viewport?.removeEventListener('resize', densityChanged),
      () => repaint.dispose(),
      () => overlay.finalize(),
      // deck.gl subscribes anonymously on a map that is not initialised yet
      // and never unsubscribes; left in place it rebuilds the finalized
      // overlay on the retained map. Only one scene exists per map, so by
      // now every listener for this event is a dead one.
      () =>
        globalThis.google?.maps?.event?.clearListeners?.(
          map,
          'renderingtype_changed',
        ),
    ],
  };
}
