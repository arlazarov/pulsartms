// Teardown has to finish.
//
// Every dispose in this tree was a bare sequence: one step that threw
// stranded the rest. That is not a lost cleanup but a permanent one - the
// `disposed` flag is set first, so nothing retries, and a half-released map
// keeps its WebGL context, its observers and its listeners for the life of
// the page.
//
// Each step runs on its own. The first failure is reported once, after
// everything else has been released.
export function releaseAll(steps, what = 'Fleet map') {
  let failure = null;
  for (const step of steps) {
    try {
      step?.();
    } catch (error) {
      failure ??= error;
    }
  }
  if (failure) console.warn(`[${what}] A teardown step failed.`, failure);
}
