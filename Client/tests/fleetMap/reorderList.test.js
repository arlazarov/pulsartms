import test from 'node:test';
import assert from 'node:assert/strict';
import { attachReorderList } from '../../Scripts/shared/reorderList.ts';

class Events {
  listeners = new Map();
  addEventListener(name, callback) {
    if (!this.listeners.has(name)) this.listeners.set(name, new Set());
    this.listeners.get(name).add(callback);
  }
  removeEventListener(name, callback) {
    this.listeners.get(name)?.delete(callback);
  }
  fire(name, values = {}) {
    const event = {
      target: this,
      pointerId: 1,
      button: 0,
      isPrimary: true,
      clientX: 20,
      clientY: 20,
      detail: 1,
      prevented: false,
      stopped: false,
      ...values,
      preventDefault() {
        this.prevented = true;
      },
      stopPropagation() {
        this.stopped = true;
      },
    };
    for (const callback of [...(this.listeners.get(name) ?? [])])
      callback(event);
    return event;
  }
  get listenerCount() {
    return [...this.listeners.values()].reduce(
      (count, entries) => count + entries.size,
      0,
    );
  }
}

class Element extends Events {
  dataset = {};
  children = [];
  attributes = new Map();
  classes = new Set();
  captures = new Set();
  disabled = false;
  disabledFieldset = false;
  isConnected = true;
  clientWidth = 240;
  clientHeight = 300;
  scrollWidth = 240;
  scrollHeight = 300;
  _scrollLeft = 0;
  _scrollTop = 0;
  rect = { left: 0, top: 0, right: 240, bottom: 300 };
  classList = {
    add: (...names) => names.forEach(name => this.classes.add(name)),
    remove: (...names) => names.forEach(name => this.classes.delete(name)),
  };
  append(child) {
    child.parent = this;
    child.ownerDocument = this.ownerDocument;
    this.children.push(child);
    return child;
  }
  contains(child) {
    return child === this || this.children.some(item => item.contains(child));
  }
  matches(selector) {
    if (selector === ':disabled') return this.disabled || this.disabledFieldset;
    if (selector === '[data-reorder-key]')
      return this.dataset.reorderKey !== undefined;
    if (selector === '[data-reorder-handle]')
      return this.dataset.reorderHandle !== undefined;
    if (selector === '[data-reorder-surface]')
      return this.dataset.reorderSurface !== undefined;
    return false;
  }
  closest(selector) {
    return this.matches(selector)
      ? this
      : (this.parent?.closest(selector) ?? null);
  }
  querySelectorAll(selector) {
    return this.children.flatMap(item => [
      ...(item.matches(selector) ? [item] : []),
      ...item.querySelectorAll(selector),
    ]);
  }
  getAttribute(name) {
    return this.attributes.get(name) ?? null;
  }
  focus() {
    this.focused = true;
  }
  setPointerCapture(id) {
    this.captures.add(id);
  }
  hasPointerCapture(id) {
    return this.captures.has(id);
  }
  releasePointerCapture(id) {
    this.captures.delete(id);
  }
  get scrollLeft() {
    return this._scrollLeft;
  }
  set scrollLeft(value) {
    this._scrollLeft = Math.max(
      0,
      Math.min(this.scrollWidth - this.clientWidth, value),
    );
  }
  get scrollTop() {
    return this._scrollTop;
  }
  set scrollTop(value) {
    this._scrollTop = Math.max(
      0,
      Math.min(this.scrollHeight - this.clientHeight, value),
    );
  }
  getBoundingClientRect() {
    const left = this.parent?.scrollLeft ?? 0;
    const top = this.parent?.scrollTop ?? 0;
    return {
      left: this.rect.left - left,
      right: this.rect.right - left,
      top: this.rect.top - top,
      bottom: this.rect.bottom - top,
    };
  }
}

function fixture(t, horizontal = false) {
  const document = new Events();
  const view = new Events();
  const frames = new Map();
  let frameId = 0;
  view.requestAnimationFrame = callback => {
    frames.set(++frameId, callback);
    return frameId;
  };
  view.cancelAnimationFrame = id => frames.delete(id);
  document.defaultView = view;
  const surface = new Element();
  surface.ownerDocument = document;
  surface.dataset.reorderSurface = '';
  if (horizontal) {
    surface.rect = { left: 0, right: 600, top: 0, bottom: 100 };
    surface.clientWidth = surface.scrollWidth = 600;
    surface.clientHeight = surface.scrollHeight = 100;
  }
  const items = ['a', 'b', 'c'].map((key, index) => {
    const row = surface.append(new Element());
    row.dataset.reorderKey = key;
    row.rect = horizontal
      ? { left: index * 200, right: index * 200 + 180, top: 0, bottom: 100 }
      : { left: 0, right: 240, top: index * 100, bottom: index * 100 + 80 };
    const handle = row.append(new Element());
    handle.dataset.reorderHandle = '';
    const icon = handle.append(new Element());
    const control = row.append(new Element());
    return { row, handle, icon, control };
  });
  const calls = [];
  const connection = attachReorderList(surface, {
    invokeMethodAsync(...args) {
      calls.push(args);
      return Promise.resolve();
    },
  });
  t.after(() => connection.dispose());
  return {
    document,
    view,
    surface,
    items,
    frames,
    calls,
    connection,
    down(index = 0, values = {}) {
      return surface.fire('pointerdown', {
        target: items[index].icon,
        ...values,
      });
    },
    move(values = {}) {
      return document.fire('pointermove', values);
    },
    up(values = {}) {
      return document.fire('pointerup', values);
    },
    tick() {
      const pending = [...frames.values()];
      frames.clear();
      pending.forEach(callback => callback());
    },
  };
}

test('pointer drag decorates source and target and sends one ordered move without mutating DOM', async t => {
  const f = fixture(t);
  const event = f.down();
  assert.equal(event.prevented, true);
  assert.equal(event.stopped, true);
  assert.equal(f.items[0].handle.focused, true);
  assert.equal(f.items[0].handle.hasPointerCapture(1), true);
  f.move({ clientY: 275 });
  assert.equal(f.items[0].row.classes.has('is-dragging'), true);
  assert.equal(f.items[2].row.classes.has('drop-after'), true);
  f.up({ clientY: 275 });
  f.up({ clientY: 275 });
  await Promise.resolve();
  assert.deepEqual(f.calls, [['OnFuelStopMoved', 'a', 'c', true]]);
  assert.deepEqual(
    f.surface.children.map(item => item.dataset.reorderKey),
    ['a', 'b', 'c'],
  );
  assert.equal(
    f.items.every(item => item.row.classes.size === 0),
    true,
  );
  assert.equal(f.document.listenerCount, 0);
  assert.equal(f.view.listenerCount, 0);
  assert.equal(f.frames.size, 0);
  assert.equal(f.items[0].handle.hasPointerCapture(1), false);
});

test('dragging upward inserts before the target', async t => {
  const f = fixture(t);
  f.down(2, { clientY: 230 });
  f.move({ clientY: 10 });
  assert.equal(f.items[0].row.classes.has('drop-before'), true);
  f.up({ clientY: 10 });
  await Promise.resolve();
  assert.deepEqual(f.calls, [['OnFuelStopMoved', 'c', 'a', false]]);
});

test('fixed pickup/delivery rows are drop targets but cannot start a drag', async t => {
  const f = fixture(t);
  delete f.items[1].handle.dataset.reorderHandle;
  f.items[1].row.dataset.reorderKey = 'stop:pickup';
  assert.equal(f.down(1, { clientY: 130 }).prevented, false);
  f.down();
  f.move({ clientY: 175 });
  f.up({ clientY: 175 });
  await Promise.resolve();
  assert.deepEqual(f.calls, [['OnFuelStopMoved', 'a', 'stop:pickup', true]]);
});

test('the final route anchor only accepts a drop before it even at its lower edge', async t => {
  const f = fixture(t);
  delete f.items[2].handle.dataset.reorderHandle;
  f.items[2].row.dataset.reorderKey = 'stop:final';
  f.items[2].row.dataset.reorderAfter = 'false';
  f.down();
  f.move({ clientY: 275 });
  assert.equal(f.items[2].row.classes.has('drop-before'), true);
  assert.equal(f.items[2].row.classes.has('drop-after'), false);
  f.up({ clientY: 275 });
  await Promise.resolve();
  assert.deepEqual(f.calls, [['OnFuelStopMoved', 'a', 'stop:final', false]]);
});

test('touch pointers reorder horizontal mobile cards by their horizontal midpoint', async t => {
  const f = fixture(t, true);
  f.down(0, { pointerType: 'touch' });
  f.move({ pointerType: 'touch', clientX: 540, clientY: 50 });
  assert.equal(f.items[2].row.classes.has('drop-after'), true);
  f.up({ pointerType: 'touch', clientX: 540, clientY: 50 });
  await Promise.resolve();
  assert.deepEqual(f.calls, [['OnFuelStopMoved', 'a', 'c', true]]);
});

test('a short tap retains normal click behavior and does not reorder', async t => {
  const f = fixture(t);
  f.down();
  f.move({ clientX: 22, clientY: 22 });
  assert.equal(f.items[0].row.classes.size, 0);
  f.up({ clientX: 22, clientY: 22 });
  await Promise.resolve();
  assert.deepEqual(f.calls, []);
  assert.equal(
    f.surface.fire('click', { target: f.items[0].icon }).prevented,
    false,
  );
});

test('drag suppresses the resulting click but does not suppress keyboard or the next pointer click', async t => {
  const f = fixture(t);
  f.down();
  f.move({ clientY: 275 });
  f.up({ clientY: 275 });
  assert.equal(
    f.surface.fire('click', { target: f.items[0].icon, detail: 0 }).prevented,
    false,
  );
  assert.equal(
    f.surface.fire('click', { target: f.items[0].icon }).prevented,
    true,
  );
  f.down();
  f.up();
  assert.equal(
    f.surface.fire('click', { target: f.items[0].icon }).prevented,
    false,
  );
  await Promise.resolve();
  assert.equal(f.calls.length, 1);
});

test('only enabled primary-pointer handles start a drag, leaving other controls and scrolling alone', t => {
  const f = fixture(t);
  const first = f.items[0];
  for (const apply of [
    () => {
      first.handle.disabled = true;
    },
    () => {
      first.handle.disabledFieldset = true;
    },
    () => {
      first.handle.attributes.set('aria-disabled', 'true');
    },
  ]) {
    apply();
    assert.equal(f.down().prevented, false);
    assert.equal(f.document.listenerCount, 0);
    first.handle.disabled = first.handle.disabledFieldset = false;
    first.handle.attributes.clear();
  }
  for (const values of [
    { button: 2 },
    { isPrimary: false },
    { target: first.control },
  ]) {
    assert.equal(f.down(0, values).prevented, false);
    assert.equal(f.document.listenerCount, 0);
  }
  first.row.dataset.reorderKey = '';
  assert.equal(f.down().prevented, false);
});

test('unrelated pointers cannot move or finish an active drag', async t => {
  const f = fixture(t);
  f.down();
  assert.equal(f.move({ pointerId: 2, clientY: 275 }).prevented, false);
  f.up({ pointerId: 2, clientY: 275 });
  assert.equal(f.items[0].row.classes.size, 0);
  f.move({ clientY: 275 });
  f.up({ clientY: 275 });
  await Promise.resolve();
  assert.equal(f.calls.length, 1);
});

for (const cancel of [
  'Escape',
  'pointercancel',
  'lostpointercapture',
  'blur',
  'dispose',
]) {
  test(`${cancel} cancels the drag and releases all transient listeners and frames`, async t => {
    const f = fixture(t);
    f.down();
    f.move({ clientY: 275 });
    assert.equal(f.frames.size, 1);
    if (cancel === 'Escape')
      assert.equal(
        f.document.fire('keydown', { key: 'Escape' }).prevented,
        true,
      );
    else if (cancel === 'blur') f.view.fire('blur');
    else if (cancel === 'dispose') {
      f.connection.dispose();
      f.connection.dispose();
    } else if (cancel === 'lostpointercapture') f.items[0].handle.fire(cancel);
    else f.document.fire(cancel);
    f.up({ clientY: 275 });
    await Promise.resolve();
    assert.deepEqual(f.calls, []);
    assert.equal(f.document.listenerCount, 0);
    assert.equal(f.view.listenerCount, 0);
    assert.equal(f.items[0].handle.listenerCount, 0);
    assert.equal(
      f.items.every(item => item.row.classes.size === 0),
      true,
    );
    assert.equal(f.frames.size, 0);
    if (cancel === 'dispose') assert.equal(f.surface.listenerCount, 0);
  });
}

test('outside drops and adjacent positions that leave order unchanged do not invoke Blazor', async t => {
  const f = fixture(t);
  for (const [source, startY, targetY] of [
    [0, 20, 110],
    [1, 120, 70],
    [0, 20, 350],
  ]) {
    f.down(source, { clientY: startY });
    f.move({ clientY: targetY });
    f.up({ clientY: targetY });
  }
  await Promise.resolve();
  assert.deepEqual(f.calls, []);
});

test('detaching or disabling the source during a drag cancels rather than submitting stale keys', async t => {
  const f = fixture(t);
  f.down();
  f.move({ clientY: 275 });
  f.items[0].handle.disabled = true;
  f.up({ clientY: 275 });
  f.items[0].handle.disabled = false;
  f.down();
  f.move({ clientY: 275 });
  f.surface.children.shift();
  f.tick();
  f.up({ clientY: 275 });
  await Promise.resolve();
  assert.deepEqual(f.calls, []);
  assert.equal(f.frames.size, 0);
});

test('a stationary non-scrolling drag does not retain an animation loop', t => {
  const f = fixture(t);
  f.down();
  f.move({ clientY: 275 });
  assert.equal(f.frames.size, 1);
  f.tick();
  assert.equal(f.frames.size, 0);
});

test('horizontal edge scrolling reveals later cards and stops at the scroll boundary', async t => {
  const f = fixture(t, true);
  f.surface.rect.right = f.surface.clientWidth = 220;
  f.surface.scrollWidth = 580;
  f.down();
  f.move({ clientX: 215, clientY: 50 });
  for (let index = 0; index < 50 && f.frames.size; index++) f.tick();
  assert.equal(f.surface.scrollLeft, 360);
  assert.equal(f.frames.size, 0);
  assert.equal(f.items[2].row.classes.has('drop-after'), true);
  f.up({ clientX: 215, clientY: 50 });
  await Promise.resolve();
  assert.deepEqual(f.calls, [['OnFuelStopMoved', 'a', 'c', true]]);
});

test('dispose between pointerup and asynchronous callback prevents a late invocation', async t => {
  const f = fixture(t);
  f.down();
  f.move({ clientY: 275 });
  f.up({ clientY: 275 });
  f.connection.dispose();
  await Promise.resolve();
  assert.deepEqual(f.calls, []);
});

test('arrow keys on enabled handles prevent scrolling without blocking Blazor or ordinary keyboard navigation', t => {
  const f = fixture(t);
  for (const key of ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight']) {
    const event = f.surface.fire('keydown', { target: f.items[0].handle, key });
    assert.equal(event.prevented, true);
    assert.equal(event.stopped, false);
  }
  for (const values of [
    { target: f.items[0].handle, key: 'Tab' },
    { target: f.items[0].handle, key: 'Enter' },
    { target: f.items[0].control, key: 'ArrowDown' },
  ]) {
    assert.equal(f.surface.fire('keydown', values).prevented, false);
  }
  f.items[0].handle.disabled = true;
  assert.equal(
    f.surface.fire('keydown', { target: f.items[0].handle, key: 'ArrowDown' })
      .prevented,
    false,
  );
});
