// Every module the page loads by name. Each is written without its
// extension: a module may be TypeScript or JavaScript while the tree is
// being converted, and the built file is called the same either way.
//
// One list, because the build and the release check must mean the same set.
// They used to keep a copy each, and when a module moved to TypeScript one
// copy learned the new extension - which left the release check looking for
// a file the build does not emit.
export const sources = [
  'fleetMap/fleetMap',
  'fleetMap/rendering/gpuScene',
  'shared/popup',
  'shared/cameraDialog',
  'shared/authStorage',
  'shared/appearance',
  'shared/reorderList',
  'shared/loadDialog',
  'shared/pageVisibility',
  'dispatch/dispatch',
  'dispatch/documents',
];

// What the build emits for each of them, under wwwroot/js/generated/.
export const builtNames = sources.map(name => `${name}.js`);
