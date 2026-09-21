import test from 'node:test';
import assert from 'node:assert/strict';
import {
  clusterTrucks,
  clusterExpansionZoom,
  clusterCamera,
} from '../../Scripts/fleetMap/rendering/truckClusters.js';
import { markerAnchor } from '../../Scripts/fleetMap/rendering/markerAnchor.ts';

const truck = (unit, lng, selected = false) => ({
  unit,
  position: [lng, 43],
  selected,
});

test('cluster click frames only its trucks inside the unobscured viewport', () => {
  const fleet = [truck('A', -77), truck('B', -77.01), truck('C', -115)];
  const group = clusterTrucks(fleet, 6).clusters[0];
  const camera = clusterCamera(group.members, 1000, 800);
  assert.ok(camera.zoom > 15);
  assert.ok(Math.abs(camera.center.lng + 77.005) < 1e-9);
  const mobile = clusterCamera(group.members, 400, 800, {
    top: 450,
    bottom: 30,
    left: 30,
    right: 30,
  });
  assert.ok(mobile.zoom < camera.zoom);
  assert.ok(
    mobile.center.lat > 43,
    'the trucks appear below the card, not beneath it',
  );
  assert.equal(clusterCamera(group.members, 0, 0), null);
  assert.equal(clusterCamera([], 1000, 800), null);
  assert.equal(clusterCamera([fleet[0], fleet[0]], 400, 800).zoom, 18);
});
test('an unchanged group keeps its label at its center through repeated zoom and polling', () => {
  const trucks = [truck('A', -77), truck('B', -77.01)];
  const initial = clusterTrucks(trucks, 4);
  for (const zoom of [6, 6.5, 9, 5, 8, 4]) {
    const updated = clusterTrucks(structuredClone(trucks), zoom);
    assert.deepEqual(updated.clusters[0].pixelOffset, [0, 0]);
    assert.deepEqual(
      updated.clusters[0].position,
      initial.clusters[0].position,
    );
  }
});
test('one expansion reaches individual trucks even from distant zoom and coincident positions', () => {
  for (const spacing of [0, 0.01, 0.4]) {
    const members = [truck('A', -77), truck('B', -77 + spacing)];
    const zoom = clusterExpansionZoom(members, 2);
    assert.equal(clusterTrucks(members, zoom).clusters.length, 0);
    assert.ok(zoom <= 12);
    if (zoom < 12)
      assert.ok(clusterTrucks(members, zoom - 1).clusters.length > 0);
  }
});
test('nearby trucks cluster at overview zoom without moving or losing source identities', () => {
  const trucks = [
    truck('11007', -77),
    truck('54777', -77.03),
    truck('11006', -115),
  ];
  const before = structuredClone(trucks),
    result = clusterTrucks(trucks, 6);
  assert.equal(result.clusters.length, 1);
  assert.deepEqual(result.clusters[0].members, trucks.slice(0, 2));
  assert.equal(result.clusters[0].count, 2);
  assert.equal(result.clusters[0].position[0], -77.015);
  assert.deepEqual(result.vehicles, [trucks[2]]);
  assert.deepEqual(trucks, before);
});
test('selected trucks never join a group and zooming in restores each individual', () => {
  const trucks = [
    truck('11005', -77, true),
    truck('11007', -77),
    truck('54777', -77.01),
  ];
  const result = clusterTrucks(trucks, 6);
  assert.deepEqual(result.vehicles, [trucks[0]]);
  assert.deepEqual(result.clusters[0].members, trucks.slice(1));
  assert.deepEqual(clusterTrucks(trucks, 12), {
    vehicles: trucks,
    clusters: [],
  });
  assert.deepEqual(clusterTrucks([], 6), { vehicles: [], clusters: [] });
});
test('cluster membership is stable across input order and shrinks with zoom', () => {
  const trucks = [truck('A', -77), truck('B', -77.4), truck('C', -90)];
  assert.deepEqual(
    clusterTrucks(trucks, 6),
    clusterTrucks([...trucks].reverse(), 6),
  );
  assert.equal(clusterTrucks(trucks, 6).clusters.length, 1);
  assert.equal(clusterTrucks(trucks, 10).clusters.length, 0);
});

test('a coincident group label is the geographic marker, not a displaced location', () => {
  const trucks = [truck('A', -77), truck('B', -77)];
  const group = clusterTrucks(trucks, 6).clusters[0];
  assert.deepEqual(group.position, [-77, 43]);
  assert.deepEqual(group.pixelOffset, [0, 0]);
});

test('group membership changes never retain a former label displacement', () => {
  const a = truck('A', -77),
    b = truck('B', -77.01),
    c = truck('C', -77.02);
  for (const members of [
    [a, b],
    [a, b, c],
    [b, c],
    [a, b],
  ]) {
    const group = clusterTrucks(members, 6).clusters[0];
    assert.equal(group.count, members.length);
    assert.deepEqual(group.pixelOffset, [0, 0]);
  }
});

test('marker connectors retain their geographic origin and bounded geometry', () => {
  for (const pixelOffset of [
    [0, -36],
    [0, 36],
    [-48, -36],
    [48, -36],
    [-48, 36],
    [48, 36],
    [0, -60],
    [0, 60],
  ]) {
    const icon = markerAnchor(pixelOffset),
      [dx, dy] = pixelOffset;
    assert.ok(Math.hypot(dx, dy) <= 60);
    assert.equal(icon.anchorX / 4, 6 - Math.min(0, dx));
    assert.equal(icon.anchorY / 4, 6 - Math.min(0, dy));
    assert.equal(icon.size, icon.height / 4);
    const svg = decodeURIComponent(icon.url);
    assert.ok(svg.includes(`M0 0 L${dx} ${dy}`));
    assert.ok(svg.includes('circle cx="0" cy="0"'));
    assert.equal(markerAnchor(pixelOffset), icon);
  }
});
