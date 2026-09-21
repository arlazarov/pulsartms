import assert from 'node:assert/strict';

/**
 * A rule that reads no files passes by saying nothing, and goes on passing.
 *
 * Three rules in this folder did exactly that. Two read only `.js` and so
 * stopped covering every module the day it moved to TypeScript - one of
 * them was the rule holding the whole tree under 300 lines, which had not
 * looked at two thirds of it for days. The third matched a list out of
 * another file's source, and that list moved.
 *
 * Every scan in this folder goes through here, so a rule that has stopped
 * finding what it is about says so instead of passing.
 */
export function scanned(what, files, least = 1) {
  assert.ok(
    files.length >= least,
    `${what}: this rule looked at ${files.length} files and expected at least ${least} - it is no longer reading what it is about`,
  );
  return files;
}
