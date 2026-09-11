import test from 'node:test';
import assert from 'node:assert/strict';
import {createDetailsCard} from '../../Scripts/fleetMap/ui/detailsCard.js';

test('fixed popup reuses content, closes explicitly and releases listeners without querying station markup', t => {
  const oldDocument = globalThis.document, oldGoogle = globalThis.google;
  t.after(() => { globalThis.document = oldDocument; globalThis.google = oldGoogle; });
  const node = () => ({
    children: [], events: new Map(), writes: 0, isConnected: false,
    setAttribute() {},
    append(...children) { this.children.push(...children); children.forEach(c => { c.isConnected = true; }); },
    replaceChildren(...children) { this.children = children; this.writes++; },
    addEventListener(name, fn) { this.events.set(name, fn); },
    removeEventListener(name) { this.events.delete(name); },
    remove() { this.isConnected = false; },
    querySelector() { throw new Error('Popup must not know station markup'); },
  });
  globalThis.document = {createElement: node};
  globalThis.google = {maps: {OverlayView: {preventMapHitsAndGesturesFrom() {}}}};
  const root = node();
  let closed = 0;
  const popup = createDetailsCard({getDiv: () => root}, {onClose() { closed++; }});
  const content = node();
  popup.show(content);
  const host = root.children[0], [close, body] = host.children;
  popup.show(content);
  assert.equal(body.writes, 1);
  close.events.get('click')();
  assert.equal(closed, 1);
  assert.equal(host.isConnected, false);
  popup.show(content);
  host.events.get('keydown')({key: 'Escape', stopPropagation() {}});
  assert.equal(closed, 2);
  popup.dispose();
  assert.equal(close.events.size, 0);
  assert.equal(host.events.size, 0);
  assert.equal(body.children.length, 0);
  popup.dispose();
  popup.show(content);
  assert.equal(host.isConnected, false);
  assert.equal(body.children.length, 0);
});
