# Styles

The stylesheets have the same shape as the markup they dress. If you know
where a component lives in `Client/`, you know where its styles live here.

```
base/      the vocabulary. Emits no CSS at all.
global/    the document itself: the reset, the :root variables, typography.
layouts/   the frame the app sits in: sidebar, main layout, auth layout.
components/  generic controls no feature owns   → mirrors Client/Components
shared/      reusable application UI             → mirrors Client/Shared
pages/       one folder per screen               → mirrors Client/Pages
```

A rule may lean on a name from a layer above it, never the other way round:
`pages` may use anything, `base` may use nothing.

## Where does a new file go?

Ask who renders the markup.

- Rendered by `Client/Components/<X>` → `components/`.
- Rendered by `Client/Shared/<Group>/<X>` → `shared/<group>/`. A group gets a
  folder once it has more than one stylesheet; until then it is a single
  file named after the component.
- Rendered by one screen only → `pages/<screen>/`. When a second screen
  wants it, move it to `shared/` - that is a `git mv` of one folder, which
  is the point of keeping each thing in its own file.
- Drawn by the map's JavaScript → still `pages/fleet-map/`. Who writes the
  markup decides, not which language writes it.

## One file, one thing

A stylesheet describes one thing you can point at: a card, the line under
it, the editor a stop opens into. When a screen has several such things,
it gets a folder, one file per part, and an `_index.scss` that names them
in the order they are emitted. That order is the cascade - keep it.

Files run to about 250 lines. Past that, the file is usually holding two
things that want separating.

## The vocabulary in `base/tokens/`

One map to a file, and the file says what belongs in it.

| file            | what it names                                     |
| --------------- | ------------------------------------------------- |
| `_space.scss`   | every gap, padding and margin                     |
| `_type.scss`    | every size text is set in                         |
| `_control.scss` | what a thing you press or type into measures      |
| `_size.scss`    | fixed dimensions more than one rule must agree on |
| `_screen.scss`  | the widths at which a layout changes shape        |
| `_layer.scss`   | what stands in front of what                      |
| `_shape.scss`   | corners and shadows                               |

`_screen.scss` holds two maps on purpose: `$screen-scale` is a handful of
steps about the window, used all over; `$screen-places` is the width at
which one named thing runs out of room, read against that thing's own box.
Adding to the scale changes how the whole product breathes; adding a place
changes only the thing it names.

Read a token with `ui.pg`, `ui.fs`, `ui.size`, `ui.theme`, `ui.radius`,
`ui.shadow`, `ui.layer`, `ui.breakpoint`. Say a boundary with
`@include ui.below(name)` / `ui.above(name)`, or, for an element's own box,
`@container <name> (width < #{ui.breakpoint(name)})`. Checks reject bare
numbers for all of these.

## A component owns how it is read

A page places a component - where it stands, how wide, how close to what is
above - and asks for one of the readings the component publishes:
`Reading="inline"`, `Reading="compact"`, `Reading="cards"`. It does not
name the component's own `__parts` from its own stylesheet, and it does not
write that component's markup without asking for a reading. Both are
checked, in `Client/tests/architecture/componentStyleOwnership.test.js`.

Anything finer than a reading is a custom property the component publishes
and documents by its default: `--stop-hours-road-size`,
`--fuel-visit-heading-display`, `--form-actions-space`.

Publish one only when a place actually sets it. Sixteen of these were
`var(--knob, default)` that nothing anywhere turned - an indirection that
read like a contract and was not one. A knob with no hand on it is a
default written twice.
