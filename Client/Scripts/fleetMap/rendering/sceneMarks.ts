import type { MarkPoint } from './truckClusters.ts';

// A point as the provider gives it: lat and lng, sometimes as methods.
type ProviderPoint = {
  lat: number | (() => number);
  lng: number | (() => number);
};

export const xy = (p: ProviderPoint): MarkPoint => [
  typeof p.lng === 'function' ? p.lng() : p.lng,
  typeof p.lat === 'function' ? p.lat() : p.lat,
];

const rgb = (c: string) =>
  c
    .replace(/^rgb\(|\)$/g, '')
    .split(',')
    .map(Number);

/**
 * What a mark needs of the scene it is drawn on. The marks below are the
 * handles the rest of the map holds - a road, a stop's badge, a truck, the
 * fuel stations - and all any of them does is record what it is now and say
 * that the scene must be drawn again.
 */
export type MarkedScene = {
  lines: Set<any>;
  stops: Set<any>;
  trucks: Set<any>;
  stations: Map<string, any>;
  disposed(): boolean;
  // A road is being chosen, so only the preview is drawn.
  editing(): boolean;
  // Something a stop is laid out from has changed, or a truck has.
  stopsChanged(): void;
  vehiclesChanged(): void;
  // The stations have changed; the scene redraws when next asked to.
  stationsChanged(): void;
  redraw(): void;
  // A mark took this click, so the map itself did not.
  clicked(): void;
  // A mark that is going away takes its hover with it.
  hoverEnded(onHover: unknown): void;
  showStations(visible: boolean): void;
  stationsSelectedBy(select: (id: string) => void): void;
};

export function createSceneMarks(scene: MarkedScene) {
  let lineId = 0,
    stopId = 0;

  // One road. It holds its own points, because the layer that draws it is
  // rebuilt only when they change.
  class Polyline {
    onClick?: (...args: unknown[]) => void;
    onMapClick: (...args: unknown[]) => boolean;
    id: string;
    path: MarkPoint[];
    data: MarkPoint[][];
    map?: unknown;
    cachedLayer?: unknown;

    constructor(options: Record<string, unknown>) {
      Object.assign(this, options);
      this.onMapClick = (...args: unknown[]) => {
        if (scene.disposed()) return false;
        if (!scene.editing()) scene.clicked();
        this.onClick?.(...args);
        return true;
      };
      this.id = `route-${++lineId}`;
      this.path = [];
      this.data = [this.path];
      scene.lines.add(this);
      scene.redraw();
    }
    setOptions(options: Record<string, unknown>) {
      Object.assign(this, options);
      scene.redraw();
    }
    setMap(value: unknown) {
      this.map = value;
      if (value) scene.lines.add(this);
      else scene.lines.delete(this);
      scene.redraw();
    }
    setPath(path: ProviderPoint[]) {
      this.path = path.map(xy);
      this.data = [this.path];
      scene.redraw();
    }
    getPath() {
      return {
        removeAt: (i: number) => {
          this.path = this.path.filter((_, index) => index !== i);
          this.data = [this.path];
          scene.redraw();
        },
        setAt: (i: number, p: ProviderPoint) => {
          this.path = this.path.slice();
          this.path[i] = xy(p);
          this.data = [this.path];
          scene.redraw();
        },
      };
    }
  }

  // The badge at a stop. Setting its map to nothing is how the provider's
  // own markers are removed, so that is how this one goes too.
  class StopMarker {
    [key: string]: any;

    constructor(options: Record<string, any>) {
      this.id = ++stopId;
      this.position = xy(options.position);
      this.number = options.number;
      this.color = options.color;
      this.onSelect = options.onSelect;
      this.onHover = options.onHover;
      this.transientLabel = options.transientLabel;
      this.job = options.job;
      this.routeRole = options.routeRole;
      this.distance = null;
      this.distanceTones = [];
      scene.stops.add(this);
      scene.stopsChanged();
    }
    setDistance(value: string | null, tones: string[] = []) {
      if (
        this.distance === value &&
        this.distanceTones.length === tones.length &&
        tones.every((tone, index) => tone === this.distanceTones[index])
      )
        return;
      this.distance = value;
      this.distanceTones = tones;
      scene.stopsChanged();
    }
    setVisible(value: boolean) {
      if (this.visible === value) return;
      this.visible = value;
      if (!value) scene.hoverEnded(this.onHover);
      scene.stopsChanged();
    }
    setNumber(value: string) {
      if (this.number === value) return;
      this.number = value;
      scene.stopsChanged();
    }
    setJob(value: string) {
      if (this.job === value) return;
      this.job = value;
      scene.stopsChanged();
    }
    setDone(value: boolean) {
      if (this.done === value) return;
      this.done = value;
      scene.stopsChanged();
    }
    get highlighted() {
      return this._highlighted === true;
    }
    set highlighted(value: boolean) {
      if (this.highlighted === value) return;
      this._highlighted = value;
      scene.stopsChanged();
    }
    set map(value: unknown) {
      if (!value) {
        if (this.onHover) scene.hoverEnded(this.onHover);
        this.onHover = null;
        this.onSelect = null;
        scene.stops.delete(this);
        scene.stopsChanged();
      }
    }
  }

  return {
    Polyline,
    StopMarker,
    // One truck. What is drawn of it is its own row in the scene, which the
    // handle writes into and nothing else reads until the next frame.
    createTruckMarker(_: unknown, onSelect: () => void) {
      const t: Record<string, any> = {
        onSelect,
        visible: true,
        unit: '',
        engine: '',
        position: null,
        heading: 0,
        speed: 0,
      };
      scene.trucks.add(t);
      return {
        update(value: { unitNumber?: string; engineState?: string }) {
          const unit = value.unitNumber || '',
            engine = value.engineState?.toLowerCase() || '';
          if (t.unit === unit && t.engine === engine) return;
          t.unit = unit;
          t.engine = engine;
          scene.vehiclesChanged();
        },
        render(p: {
          latitude: number;
          longitude: number;
          heading?: number;
          speed?: number;
        }) {
          if (
            !p ||
            (t.position?.[0] === p.longitude &&
              t.position?.[1] === p.latitude &&
              t.heading === (p.heading || 0) &&
              t.speed === (p.speed || 0))
          )
            return;
          t.position = [p.longitude, p.latitude];
          t.heading = p.heading || 0;
          t.speed = p.speed || 0;
          scene.vehiclesChanged();
        },
        setVisible(value: boolean) {
          if (t.visible === value) return;
          t.visible = value;
          scene.vehiclesChanged();
        },
        setSelected(value: boolean) {
          if (t.selected === value) return;
          t.selected = value;
          scene.vehiclesChanged();
        },
        dispose() {
          scene.trucks.delete(t);
          scene.vehiclesChanged();
        },
      };
    },
    // Every fuel station at once: there are thousands of them, so they are
    // one layer written point by point rather than a handle each.
    createStationPointLayer(_: unknown, onSelect: (id: string) => void) {
      scene.stationsSelectedBy(onSelect);
      return {
        setPoint(
          id: string,
          p: { lat: number; lng: number },
          c: string,
          recommended: boolean,
          selected: boolean,
          numbers = '',
          editing = false,
          price: number | null = null,
        ) {
          const old = scene.stations.get(id);
          if (
            old &&
            old.position[0] === p.lng &&
            old.position[1] === p.lat &&
            old.sourceColor === c &&
            old.recommended === recommended &&
            old.selected === selected &&
            old.numbers === numbers &&
            old.editing === editing &&
            old.price === price
          )
            return;
          scene.stations.set(id, {
            id,
            position: [p.lng, p.lat],
            color: rgb(c),
            sourceColor: c,
            recommended,
            selected,
            numbers,
            editing,
            price,
          });
          scene.stationsChanged();
        },
        removePoint(id: string) {
          if (scene.stations.delete(id)) scene.stationsChanged();
        },
        redraw: () => scene.redraw(),
        setVisible(value: boolean) {
          scene.showStations(value);
        },
        hitTest() {
          return null;
        },
        dispose() {
          scene.stations.clear();
          scene.stationsChanged();
          scene.redraw();
        },
      };
    },
  };
}
