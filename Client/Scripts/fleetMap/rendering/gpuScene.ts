import { GoogleMapsOverlay } from '@deck.gl/google-maps';
import {
  ScatterplotLayer,
  PathLayer,
  IconLayer,
  TextLayer,
} from '@deck.gl/layers';
import { PathStyleExtension } from '@deck.gl/extensions';
import { createScene } from './scene.js';

// Keep the vendor boundary separate so scene lifecycle is testable without WebGL.
export function createGpuScene(map: google.maps.Map) {
  const routeDashExtensions = [
    new PathStyleExtension({ dash: true, highPrecisionDash: true }),
  ];
  return createScene(map, {
    GoogleMapsOverlay,
    ScatterplotLayer,
    PathLayer,
    IconLayer,
    TextLayer,
    routeDashExtensions,
  });
}
