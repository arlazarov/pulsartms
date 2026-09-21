import test from 'node:test';
import assert from 'node:assert/strict';
import { createTruckLayer } from '../../Scripts/fleetMap/trucks/truckLayer.ts';
import { truckPlaybackDelay } from '../../Scripts/fleetMap/trucks/truckPlayback.ts';

function playback(t) {
  let now = Date.UTC(2026, 8, 15, 5),
    frameId = 0;
  const frames = new Map(),
    events = new Map(),
    cameras = [],
    rendered = [];
  const original = new Map(
    [
      'document',
      'performance',
      'requestAnimationFrame',
      'cancelAnimationFrame',
    ].map(name => [name, Object.getOwnPropertyDescriptor(globalThis, name)]),
  );
  const document = {
    hidden: false,
    addEventListener: (name, fn) => events.set(name, fn),
    removeEventListener: name => events.delete(name),
  };
  Object.defineProperties(globalThis, {
    document: { configurable: true, value: document },
    performance: { configurable: true, value: { now: () => now } },
    requestAnimationFrame: {
      configurable: true,
      value: fn => {
        frames.set(++frameId, fn);
        return frameId;
      },
    },
    cancelAnimationFrame: {
      configurable: true,
      value: id => frames.delete(id),
    },
  });
  t.mock.method(Date, 'now', () => now);
  const origin = now - truckPlaybackDelay;
  const point = time => ({
    truckId: '54777',
    truckExternalId: 'gps-54777',
    updatedAt: new Date(time).toISOString(),
    latitude: 38 + (time - origin) / 3600000,
    longitude: -79,
    heading: 0,
    speed: 69,
  });
  const layer = createTruckLayer(
    {
      addListener: () => ({ remove() {} }),
      moveCamera: camera => cameras.push(camera),
    },
    undefined,
    undefined,
    undefined,
    () => ({
      render: position => rendered.push(position),
      update() {},
      setVisible() {},
      setSelected() {},
      dispose() {},
    }),
  );
  layer.setInitialTruck('54777');
  const publish = (start, end) => {
    const points = [];
    for (let time = start; time <= end; time += 5000) points.push(point(time));
    layer.setTrucks([point(end)], points);
  };
  const advance = milliseconds => {
    now += milliseconds;
  };
  const frame = milliseconds => {
    advance(milliseconds);
    const pending = [...frames.values()];
    frames.clear();
    for (const callback of pending) callback();
  };
  const visibility = hidden => {
    document.hidden = hidden;
    events.get('visibilitychange')();
  };
  t.after(() => {
    layer.dispose();
    for (const [name, descriptor] of original) {
      if (descriptor) Object.defineProperty(globalThis, name, descriptor);
      else delete globalThis[name];
    }
  });
  return {
    layer,
    frames,
    events,
    rendered,
    cameras,
    origin,
    point,
    publish,
    frame,
    advance,
    visibility,
    now: () => now,
    position: () => layer.getPosition('54777'),
  };
}

test('returning after hidden telemetry pruning rebases before paint without a catch-up flight', t => {
  const f = playback(t);
  f.publish(f.origin - 60000, f.now());
  f.layer.setFollow('54777', true);
  f.frame(16);
  f.visibility(true);
  assert.equal(f.frames.size, 0);
  const painted = f.rendered.length,
    cameras = f.cameras.length;
  f.advance(600000);
  f.publish(f.now() - 180000, f.now());
  assert.equal(f.rendered.length, painted);
  assert.equal(f.cameras.length, cameras);
  f.visibility(false);
  const expected = f.now() - truckPlaybackDelay;
  assert.equal(f.position().gpsTime, expected);
  assert.equal(f.position().latitude, f.point(expected).latitude);
  assert.equal(f.layer.isFollowing(), true);
  assert.equal(f.cameras.at(-1).center.lat, f.position().latitude);
  const from = f.position();
  f.frame(100);
  assert.equal(f.position().gpsTime - from.gpsTime, 100);
  assert.ok(
    Math.abs(f.position().latitude - from.latitude - 100 / 3600000) < 1e-10,
  );
});

test('resume before the first fresh snapshot cannot animate across subsequently pruned history', t => {
  const f = playback(t);
  f.publish(f.origin - 60000, f.now());
  f.visibility(true);
  f.advance(600000);
  f.visibility(false);
  f.publish(f.now() - 180000, f.now());
  const expected = f.now() - truckPlaybackDelay;
  assert.equal(f.position().gpsTime, expected);
  assert.equal(f.position().latitude, f.point(expected).latitude);
  f.frame(16);
  assert.equal(f.position().gpsTime, expected + 16);
});

test('a suspended frame without a visibility event rebases instead of replaying minutes', t => {
  const f = playback(t);
  f.publish(f.origin - 60000, f.now());
  f.advance(180000);
  f.publish(f.now() - 180000, f.now());
  assert.equal(f.position().gpsTime, f.now() - truckPlaybackDelay);
  f.frame(16);
  assert.equal(f.position().gpsTime, f.now() - truckPlaybackDelay);
});

test('minute batches plus polling jitter preserve normal speed without artificial stops', t => {
  const f = playback(t);
  f.publish(f.origin - 60000, f.now());
  let previous = f.position().gpsTime;
  for (let elapsed = 100; elapsed <= 240000; elapsed += 100) {
    f.frame(100);
    if (elapsed % 80000 === 0) f.publish(f.now() - 180000, f.now() - 5000);
    assert.equal(
      f.position().gpsTime - previous,
      100,
      `playback at ${elapsed}ms`,
    );
    previous = f.position().gpsTime;
  }
});

test('real telemetry exhaustion holds the last point and releases the frame loop', t => {
  const f = playback(t);
  f.publish(f.origin - 60000, f.origin);
  assert.equal(f.position().gpsTime, f.origin);
  assert.equal(f.frames.size, 0);
  f.advance(600000);
  f.publish(f.origin - 60000, f.origin);
  assert.equal(f.position().gpsTime, f.origin);
  assert.equal(f.frames.size, 0);
  f.layer.dispose();
  assert.equal(f.events.size, 0);
});

test('hiding while the GPS buffer is exhausted resumes fresh movement without replay', t => {
  const f = playback(t);
  f.publish(f.origin - 60000, f.origin);
  f.layer.setFollow('54777', true);
  assert.equal(f.frames.size, 0);
  f.visibility(true);
  f.advance(600000);
  f.publish(f.now() - 180000, f.now());
  f.visibility(false);
  assert.equal(f.position().gpsTime, f.now() - truckPlaybackDelay);
  f.frame(100);
  assert.equal(f.position().gpsTime, f.now() - truckPlaybackDelay);
  assert.equal(f.layer.isFollowing(), true);
});
