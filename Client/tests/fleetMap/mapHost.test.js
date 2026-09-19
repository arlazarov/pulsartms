import test from 'node:test';
import assert from 'node:assert/strict';
import { createMapHost } from '../../Scripts/fleetMap/provider/mapHost.js';

function container() {
  return {
    ownerDocument: {
      createElement: () => ({
        remove() {
          this.parent = null;
        },
      }),
    },
    append(host) {
      host.parent = this;
    },
  };
}

test('theme changes replace the map once and preserve its last camera', () => {
  const options = [];
  const mount = createMapHost((_host, value) => {
    options.push(value);
    return {
      getCenter: () => ({ lat: 35, lng: -99 }),
      getZoom: () => 9,
      setOptions(next) {
        assert.equal(next.colorScheme, undefined);
      },
    };
  });
  const first = mount(container(), { colorScheme: 'LIGHT' });
  first.release();
  const second = mount(container(), { colorScheme: 'DARK', zoom: 5 });
  assert.notEqual(first.map, second.map);
  assert.deepEqual(options[1], {
    colorScheme: 'DARK',
    center: { lat: 35, lng: -99 },
    zoom: 9,
  });
  second.release();
  const third = mount(container(), { colorScheme: 'DARK' });
  assert.equal(third.map, second.map);
  assert.equal(options.length, 2);
  third.release();
});

test('provider host is bounded and reuse does not move the camera before fleet data arrives', () => {
  let creations = 0,
    host;
  const map = {
    setOptions(options) {
      this.options = options;
    },
  };
  const mount = createMapHost(node => {
    creations++;
    host = node;
    return map;
  });
  const first = mount(container(), {});
  assert.throws(() => mount(container(), {}), /already mounted/);
  first.release();
  assert.equal(host.parent, null);
  const target = container();
  const second = mount(target, {
    center: { lat: 41.5, lng: -87.5 },
    zoom: 5,
    mapId: 'id',
    renderingType: 'VECTOR',
    mapTypeId: 'roadmap',
  });
  first.release();
  assert.equal(host.parent, target);
  assert.equal(creations, 1);
  assert.equal(first.map, second.map);
  assert.deepEqual(map.options, { mapTypeId: 'roadmap' });
  second.release();
});

test('provider initialization can be retried after failure', () => {
  let attempts = 0;
  const mount = createMapHost(() => {
    if (++attempts === 1) throw new Error('Failed');
    return {};
  });
  assert.throws(() => mount(container(), {}), /Failed/);
  mount(container(), {}).release();
});

function startup(t) {
  const listeners = new Map(),
    timers = new Map();
  let nextTimer = 0,
    host;
  t.mock.method(globalThis, 'setTimeout', (callback, milliseconds) => {
    assert.equal(milliseconds, 2000);
    timers.set(++nextTimer, callback);
    return nextTimer;
  });
  t.mock.method(globalThis, 'clearTimeout', id => timers.delete(id));
  const mount = createMapHost(node => {
    host = node;
    assert.equal(host.className, 'fleet-map-host fleet-map-host--initializing');
    return {
      setOptions() {},
      addListener(name, callback) {
        listeners.set(name, callback);
        return {
          remove() {
            if (listeners.get(name) === callback) listeners.delete(name);
          },
        };
      },
    };
  });
  const first = mount(container(), {});
  t.after(() => first.release());
  return { mount, first, listeners, timers, host };
}

test('first camera remains hidden until idle and polling cannot rehide it', t => {
  const { first, listeners, timers, host } = startup(t);
  let fitted = 0;
  first.initialCamera(() => {
    fitted++;
  });
  assert.equal(fitted, 1);
  assert.match(host.className, /initializing/);
  listeners.get('idle')();
  assert.equal(host.className, 'fleet-map-host');
  assert.equal(listeners.size, 0);
  assert.equal(timers.size, 0);
  first.initialCamera(initial => {
    assert.equal(initial, false);
    fitted++;
  });
  assert.equal(host.className, 'fleet-map-host');
  assert.equal(listeners.size, 0);
  assert.equal(fitted, 2);
});

test('empty data, failure, an immediate truck camera and a no-idle fallback all reveal safely', t => {
  const { mount, first, listeners, timers, host } = startup(t);
  first.initialCamera();
  assert.equal(host.className, 'fleet-map-host');
  first.release();
  const failed = mount(container(), {});
  failed.show();
  assert.equal(host.className, 'fleet-map-host');
  failed.release();
  const direct = mount(container(), {});
  direct.initialCamera(initial => assert.equal(initial, true), false);
  assert.equal(host.className, 'fleet-map-host');
  assert.equal(timers.size, 0);
  direct.release();
  const unchanged = mount(container(), {});
  unchanged.initialCamera(() => {});
  [...timers.values()][0]();
  assert.equal(host.className, 'fleet-map-host');
  assert.equal(listeners.size, 0);
  assert.equal(timers.size, 0);
  unchanged.release();
});

test('disposed startup callbacks cannot reveal a later mount and dispose clears wait resources', t => {
  const { mount, first, listeners, timers, host } = startup(t);
  first.initialCamera(() => {});
  const oldIdle = listeners.get('idle'),
    oldFallback = [...timers.values()][0];
  first.release();
  assert.equal(listeners.size, 0);
  assert.equal(timers.size, 0);
  const second = mount(container(), {});
  oldIdle();
  oldFallback();
  first.show();
  assert.match(host.className, /initializing/);
  assert.throws(
    () =>
      second.initialCamera(() => {
        throw new Error('Camera failed');
      }),
    /Camera failed/,
  );
  assert.equal(host.className, 'fleet-map-host');
  assert.equal(listeners.size, 0);
  assert.equal(timers.size, 0);
  second.release();
});
