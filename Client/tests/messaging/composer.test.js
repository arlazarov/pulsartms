import test from 'node:test';
import assert from 'node:assert/strict';

// Enough of the DOM for the reply box: a root that dispatches to its
// listeners, a text box inside a form, and a file input.
class Target {
  listeners = new Map();
  addEventListener(type, listener) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }
  removeEventListener(type, listener) {
    this.listeners.set(
      type,
      (this.listeners.get(type) ?? []).filter(x => x !== listener),
    );
  }
  dispatchEvent(event) {
    for (const listener of this.listeners.get(event.type) ?? [])
      listener(event);
    return true;
  }
}

class TextArea extends Target {
  value = '';
  selectionStart = 0;
  selectionEnd = 0;
  inputs = 0;
  constructor(form) {
    super();
    this.form = form;
  }
  hasAttribute(name) {
    return name === 'data-send-on-enter';
  }
  setRangeText(text, start, end) {
    this.value = this.value.slice(0, start) + text + this.value.slice(end);
    this.selectionStart = this.selectionEnd = start + text.length;
  }
  dispatchEvent(event) {
    if (event.type === 'input') this.inputs++;
    return super.dispatchEvent(event);
  }
}

globalThis.HTMLTextAreaElement = TextArea;
globalThis.Event ??= class {
  constructor(type) {
    this.type = type;
  }
};

const { attach } = await import('../../Scripts/messages/composer.ts');

function composer({ staged = false } = {}) {
  const form = {
    submits: 0,
    requestSubmit() {
      this.submits++;
    },
    querySelector: selector =>
      selector === '[data-staged]' && staged ? {} : null,
  };
  const box = new TextArea(form);
  const file = Object.assign(new Target(), { disabled: false, files: null });
  const classes = new Set();
  const root = Object.assign(new Target(), {
    classList: {
      add: x => classes.add(x),
      remove: x => classes.delete(x),
      toggle: (x, on) => (on ? classes.add(x) : classes.delete(x)),
    },
    contains: () => false,
    querySelector: () => file,
  });
  const handle = attach(root);
  const key = options => {
    const event = {
      type: 'keydown',
      key: 'Enter',
      target: box,
      prevented: false,
      preventDefault() {
        this.prevented = true;
      },
      ...options,
    };
    root.dispatchEvent(event);
    return event;
  };
  return { form, box, file, root, classes, handle, key };
}

test('Enter sends and Option+Enter starts a new line', () => {
  const { form, box, key } = composer();
  box.value = 'On my way';
  box.selectionStart = box.selectionEnd = 9;

  assert.equal(key({}).prevented, true);
  assert.equal(form.submits, 1);

  const newline = key({ altKey: true });
  assert.equal(newline.prevented, true);
  assert.equal(box.value, 'On my way\n');
  assert.equal(box.inputs, 1);
  assert.equal(form.submits, 1);

  assert.equal(key({ shiftKey: true }).prevented, false);
  assert.equal(form.submits, 1);
});

test('a key an input method is composing is left to it', () => {
  const { form, box, key } = composer();
  box.value = 'にほん';

  assert.equal(key({ isComposing: true }).prevented, false);
  assert.equal(key({ keyCode: 229 }).prevented, false);
  assert.equal(form.submits, 0);
});

test('an empty box sends only when files are staged', () => {
  assert.equal(composer().key({}).prevented, true);
  const empty = composer();
  empty.key({});
  assert.equal(empty.form.submits, 0);
  const staged = composer({ staged: true });
  staged.key({});
  assert.equal(staged.form.submits, 1);
});

test('dropped files go to the file input, and nothing after dispose', () => {
  const { root, file, classes, handle } = composer();
  let changes = 0;
  file.addEventListener('change', () => changes++);
  const files = { length: 1 };
  const drag = type => {
    const event = {
      type,
      dataTransfer: { types: ['Files'], files, dropEffect: '' },
      prevented: false,
      preventDefault() {
        this.prevented = true;
      },
    };
    root.dispatchEvent(event);
    return event;
  };

  assert.equal(drag('dragover').prevented, true);
  assert.ok(classes.has('is-dropping'));
  assert.equal(drag('drop').prevented, true);
  assert.equal(file.files, files);
  assert.equal(changes, 1);
  assert.ok(!classes.has('is-dropping'));

  handle.dispose();
  drag('drop');
  assert.equal(changes, 1);
});
