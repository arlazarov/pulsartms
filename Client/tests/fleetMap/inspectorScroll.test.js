import test from 'node:test';
import assert from 'node:assert/strict';
import { watchInspectorScroll } from '../../Scripts/fleetMap/ui/inspectorScroll.ts';

// A card that shows something else opens from its head; a rerender of the
// same card keeps the reader's place (Dispatch, September 28).
function fixture() {
  const observers = [];
  class FakeObserver {
    constructor(callback) {
      this.callback = callback;
      this.connected = false;
      observers.push(this);
    }
    observe(target, options) {
      this.target = target;
      this.options = options;
      this.connected = true;
    }
    disconnect() {
      this.connected = false;
    }
  }
  const attributes = { 'data-inspector-mode': 'truck' };
  const inspector = {
    scrollTop: 0,
    ownerDocument: { defaultView: { MutationObserver: FakeObserver } },
    getAttribute: name => attributes[name] ?? null,
  };
  const stage = {
    querySelector: selector =>
      selector === '.fleet-map-info-reserved' ? inspector : null,
  };
  const setMode = mode => {
    const oldValue = attributes['data-inspector-mode'];
    attributes['data-inspector-mode'] = mode;
    for (const observer of observers.filter(x => x.connected))
      observer.callback([{ attributeName: 'data-inspector-mode', oldValue }]);
  };
  return { observers, inspector, stage, setMode };
}

test('a card of another kind opens from its head', () => {
  const { observers, inspector, stage, setMode } = fixture();
  const stop = watchInspectorScroll(stage);
  assert.deepEqual(observers[0].options, {
    attributes: true,
    attributeFilter: ['data-inspector-mode'],
    attributeOldValue: true,
  });
  setMode('stop');
  inspector.scrollTop = 97;
  setMode('truck');
  assert.equal(inspector.scrollTop, 0);
  // The same card set again keeps the reader's place.
  inspector.scrollTop = 40;
  setMode('truck');
  assert.equal(inspector.scrollTop, 40);
  stop();
  assert.equal(observers[0].connected, false);
  setMode('fuel');
  assert.equal(inspector.scrollTop, 40);
});

test('no card and no observer is no work', () => {
  assert.doesNotThrow(() => watchInspectorScroll(null)());
  assert.doesNotThrow(() =>
    watchInspectorScroll({ querySelector: () => null })(),
  );
});
