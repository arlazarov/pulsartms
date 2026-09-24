# Cohesion review

Size is a signal to look, never a verdict. No check fails because a file,
class, stylesheet or module is long, and none fails because it is short.
There is no size exception to record. What a reviewer judges is whether a
unit has one owner and can be understood, tested and changed as one thing.

## What to review

For a class (with all its partial files), a component, a module or a
stylesheet:

- **Owner.** Which module owns it, and does all of it belong to that owner?
  Code serving another module's decision belongs to that module.
- **Independent responsibilities.** How many things does it do that could
  change for unrelated reasons? Steps of one pipeline are one thing;
  a read, a cache and a notification protocol are three.
- **Dependencies and coupling.** What it needs, and whether each dependency
  is used for its own concern or only passed through.
- **Public surface.** What callers can reach, and whether they need all of
  it.
- **Lifecycle and state.** Whether its state has one lifetime (request,
  scope, process) and one guard, or several mixed together.
- **Cost of verification and change.** Whether a test can exercise one
  responsibility without constructing the rest, and whether a change to one
  concern forces reading the others.

## Splitting and joining

- **A partial does not fix a bloated class.** Files of one partial class are
  still one class: one set of fields, one surface, one reviewer's load.
  Moving methods into `Foo.Part.cs` changes the file count, not the design.
  Partials are fine for a class that is cohesive and long, named for the
  concern each file holds.
- **Extract a real owner** when responsibilities are mixed: a type with its
  own name, dependencies and tests, called through a surface smaller than
  what it replaced.
- **Join pieces** that were split only for a count: a residue file of two
  helpers, a forwarding class that adds no decision, a partial that holds
  one method of the flow in the next file.
- **Do not replace a line limit** with an equally blind limit on methods,
  parameters or dependencies. Numbers from the source inventory
  (`scripts/source-inventory.mjs`) point at candidates; the review decides.
- **Never compress formatting** or delete useful explanations to change a
  number.

## Checks that stay

Layer, dependency, module-ownership, authentication, tenant, style-token,
component-ownership and behavior checks are unaffected. This policy
replaces only the counting rules that failed on length: the server 400-line
review with its reviewed maximums, the browser module 300-line and
shrink-only budgets, and the stylesheet 280-line limit.
