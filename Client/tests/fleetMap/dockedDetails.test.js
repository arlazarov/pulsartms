import test from 'node:test';
import assert from 'node:assert/strict';
import {createDockedDetails} from '../../Scripts/fleetMap/ui/dockedDetails.js';

function fixture() {
  const changes = [], host = {children: [], writes: 0, events: new Map(),
    replaceChildren(...children) { this.children = children; this.writes++; },
    addEventListener(name, callback) { this.events.set(name, callback); },
    removeEventListener(name) { this.events.delete(name); }};
  const inspector = createDockedDetails(host, (kind, version) => changes.push([kind, version]));
  const stop = inspector.popupFactory('stop')({}, {onClose: () => stop.hide()});
  const fuel = inspector.popupFactory('fuel')({}, {onClose: () => fuel.hide()});
  return {host, inspector, stop, fuel, changes};
}

test('docked cards reuse only the explicit shared host, with no shell or copied content', () => {
  const {host, inspector, stop, changes} = fixture(), content = {node: 'existing stop HTML'};
  stop.show(content);
  assert.deepEqual(host.children, [], 'polling cannot open an inactive inspector');
  inspector.activate('stop');
  stop.show(content); stop.show(content);
  assert.deepEqual(host.children, [content]);
  assert.equal(host.writes, 1);
  assert.deepEqual(changes, [['stop', 1]]);
  stop.show({node: 'refreshed ETA HTML'});
  assert.equal(host.writes, 2, 'active metadata refresh replaces only native content');
  assert.equal(changes.length, 1, 'polling does not switch inspector mode');
});

test('late show, hide and disposal from an old owner cannot erase or reopen another card', () => {
  const {host, inspector, stop, fuel, changes} = fixture();
  inspector.activate('stop'); stop.show({node: 'stop'});
  inspector.activate('fuel'); const content = {node: 'fuel'}; fuel.show(content);
  stop.hide(); stop.show({node: 'late stop'}); stop.dispose();
  assert.deepEqual(host.children, [content]);
  assert.deepEqual(changes, [['stop', 1], ['fuel', 2]]);
  inspector.setMode('next-stop');
  fuel.show({node: 'fuel polling'}); fuel.hide();
  assert.deepEqual(host.children, []);
  assert.equal(inspector.mode, 'next-stop');
  inspector.setMode('truck');
  inspector.activate('stop');
  assert.equal(inspector.mode, 'truck', 'disposed owners cannot acquire the shared host');
});

test('native Escape closes once, monotonically revisions transitions and disposal releases content/listeners', () => {
  const {host, inspector, stop, fuel, changes} = fixture();
  inspector.activate('fuel'); fuel.show({node: 'fuel'});
  let stopped = 0;
  host.events.get('keydown')({key: 'Escape', stopPropagation() { stopped++; }});
  assert.equal(stopped, 1);
  assert.deepEqual(host.children, []);
  assert.deepEqual(changes, [['fuel', 1], ['closed', 2]]);
  inspector.activate('stop'); stop.show({node: 'stop'});
  inspector.dispose(); inspector.dispose();
  inspector.activate('fuel'); inspector.setMode('truck'); stop.show({node: 'late'});
  assert.equal(host.events.size, 0);
  assert.deepEqual(host.children, []);
  assert.deepEqual(changes.map(([, version]) => version), [1, 2, 3]);
});

test('explicit Back, Close and native Escape restore only departing inspector focus without scrolling', () => {
  const document = {activeElement: null}, focused = [], inside = {}, outside = {};
  const shell = {contains: node => node === inside};
  const host = {ownerDocument: document, closest: () => shell, replaceChildren() {}, events: new Map(),
    addEventListener(name, callback) {this.events.set(name, callback);}, removeEventListener() {}};
  const map = {focus(options) {focused.push(options); document.activeElement = outside;}};
  const inspector = createDockedDetails(host, () => {}, () => 'truck', map);
  const fuel = inspector.popupFactory('fuel')({}, {onClose: () => fuel.hide()});
  inspector.activate('fuel'); fuel.show({}); document.activeElement = inside;
  fuel.show({});
  assert.equal(focused.length, 0, 'polling keeps the current keyboard location');
  inspector.setMode('truck', true);
  assert.deepEqual(focused, [{preventScroll: true}]);
  inspector.activate('fuel'); fuel.show({}); document.activeElement = inside;
  host.events.get('keydown')({key: 'Escape', stopPropagation() {}});
  assert.equal(inspector.mode, 'truck');
  assert.equal(focused.length, 2);
  inspector.activate('fuel'); fuel.show({}); document.activeElement = inside;
  inspector.setMode('closed', true);
  assert.equal(focused.length, 3);
  document.activeElement = outside;
  inspector.setMode('closed', true);
  inspector.activate('fuel'); fuel.show({}); fuel.hide();
  assert.equal(focused.length, 3, 'pointer markers and passive owner cleanup do not steal focus');
});
