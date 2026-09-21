import test from 'node:test';
import assert from 'node:assert/strict';
import {
  show,
  close,
  dispose,
  rememberOverview,
  focusView,
} from '../../Scripts/shared/loadDialog.ts';

test('selected-stop navigation retains overview scroll and restores focus without a nested dialog', () => {
  const content = { scrollTop: 340 },
    focus = [];
  const opener = {
    isConnected: true,
    focus: options => focus.push(['opener', options]),
  };
  const back = { focus: options => focus.push(['back', options]) };
  const dialog = {
    style: {},
    ownerDocument: { activeElement: opener },
    getBoundingClientRect: () => ({ height: 600 }),
    querySelector: selector =>
      selector.endsWith('__content') ? content : back,
  };
  rememberOverview(dialog);
  focusView(dialog, true);
  assert.equal(content.scrollTop, 0);
  assert.equal(dialog.style.height, '600px');
  content.scrollTop = 140;
  focusView(dialog, false);
  assert.equal(content.scrollTop, 340);
  assert.equal(dialog.style.height, '');
  assert.deepEqual(focus, [
    ['back', { preventScroll: true }],
    ['opener', { preventScroll: true }],
  ]);
});

function fixture(t) {
  const previous = globalThis.document;
  const previousWindow = globalThis.window;
  const opener = {
    isConnected: true,
    calls: [],
    getAttribute() {
      return 'load-1';
    },
    focus(options) {
      this.calls.push(options);
    },
  };
  const htmlClasses = new Set(),
    bodyClasses = new Set(),
    properties = new Map(),
    controlled = [];
  globalThis.window = {
    scrollY: 500,
    scrollTo(x, y) {
      this.scrollY = y;
    },
  };
  globalThis.document = {
    activeElement: opener,
    documentElement: {
      classList: {
        add: value => htmlClasses.add(value),
        remove: value => htmlClasses.delete(value),
      },
    },
    body: {
      classList: {
        add: value => bodyClasses.add(value),
        remove: value => bodyClasses.delete(value),
      },
      style: {
        setProperty: (name, value) => properties.set(name, value),
        removeProperty: name => properties.delete(name),
      },
    },
    getElementById: id => controlled.find(element => element.id === id),
    querySelectorAll: () => controlled,
  };
  class Dialog extends EventTarget {
    isConnected = true;
    open = false;
    shows = 0;
    listeners = new Set();
    addEventListener(name, callback) {
      super.addEventListener(name, callback);
      this.listeners.add(callback);
    }
    removeEventListener(name, callback) {
      super.removeEventListener(name, callback);
      this.listeners.delete(callback);
    }
    showModal() {
      this.open = true;
      this.shows++;
    }
    close() {
      this.open = false;
      this.dispatchEvent(new Event('close'));
    }
    getBoundingClientRect() {
      return { left: 100, right: 600, top: 100, bottom: 700 };
    }
    pointer(name, x, y) {
      const event = new Event(name);
      Object.assign(event, { clientX: x, clientY: y });
      this.dispatchEvent(event);
    }
  }
  const dialog = new Dialog();
  t.after(() => {
    dispose(dialog);
    globalThis.document = previous;
    globalThis.window = previousWindow;
  });
  return { dialog, opener, htmlClasses, bodyClasses, properties, controlled };
}

test('load modal opens once, retains native Escape and restores its connected opener', t => {
  const { dialog, opener, htmlClasses, bodyClasses, properties } = fixture(t);
  show(dialog);
  show(dialog);
  assert.equal(dialog.shows, 1);
  assert.equal(htmlClasses.has('popup-open'), true);
  assert.equal(bodyClasses.has('popup-open'), true);
  assert.equal(properties.get('--popup-scroll-offset'), '-500px');
  const cancel = new Event('cancel', { cancelable: true });
  assert.equal(dialog.dispatchEvent(cancel), true);
  assert.equal(cancel.defaultPrevented, false);
  dialog.close();
  assert.deepEqual(opener.calls, [{ preventScroll: true }]);
  assert.equal(htmlClasses.has('popup-open'), false);
  assert.equal(bodyClasses.has('popup-open'), false);
  assert.equal(properties.has('--popup-scroll-offset'), false);
  assert.equal(window.scrollY, 500);
  dispose(dialog);
  assert.equal(dialog.listeners.size, 0);
});

test('close returns focus to the same Papers identity when polling recreates its opener', t => {
  const { dialog, opener, controlled } = fixture(t);
  show(dialog);
  opener.isConnected = false;
  const replacement = {
    isConnected: true,
    calls: [],
    getAttribute() {
      return 'load-1';
    },
    focus(options) {
      this.calls.push(options);
    },
  };
  controlled.push(replacement);
  close(dialog);
  assert.deepEqual(replacement.calls, [{ preventScroll: true }]);
});

test('only a complete backdrop gesture closes the modal, not content clicks or drags', t => {
  const { dialog } = fixture(t);
  show(dialog);
  dialog.pointer('pointerdown', 150, 150);
  dialog.pointer('click', 150, 150);
  assert.equal(dialog.open, true);
  dialog.pointer('pointerdown', 150, 150);
  dialog.pointer('click', 50, 50);
  assert.equal(dialog.open, true);
  dialog.pointer('pointerdown', 50, 50);
  dialog.pointer('click', 50, 50);
  assert.equal(dialog.open, false);
});

test('unmount closes an open modal and releases handlers without focusing a removed opener', t => {
  const { dialog, opener } = fixture(t);
  show(dialog);
  opener.isConnected = false;
  dispose(dialog);
  dispose(dialog);
  close(dialog);
  assert.equal(dialog.open, false);
  assert.equal(dialog.listeners.size, 0);
  assert.equal(opener.calls.length, 0);
});
