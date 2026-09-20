import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const css = compileString(
  "@use 'components/popup'; @use 'shared/trucks/camera'; @use 'shared/dispatch/load-dialog'; @use 'shared/fuel/plan-editor'; @use 'shared/route-editor'; @use 'pages/fleet-map/stage'; @use 'pages/fleet-map/inspector';",
  {
    loadPaths: [fileURLToPath(new URL('../../Styles/', import.meta.url))],
  },
).css;

test('all overlay headings share readable semantic typography without changing content-specific sizing', () => {
  for (const selector of [
    '.popup__title',
    '.truck-camera header > strong',
    '.dispatch-load-dialog .dispatch-paper__title h2',
    '.fuel-plan-editor__header > div > strong',
    '.route-editor__header > strong',
    '.fleet-map-inspector__title',
  ]) {
    const block = css.slice(css.indexOf(selector + ' {')).split('}')[0];
    for (const declaration of [
      'color: var(--ui-text);',
      'font-size: var(--type-lead);',
      'font-weight: 600;',
      'line-height: 1.4;',
    ])
      assert.ok(block.includes(declaration), `${selector}: ${declaration}`);
  }
});

test('map and editor dismiss controls use the shared square icon control', () => {
  for (const selector of [
    '.truck-camera header > button',
    '.dispatch-load-dialog .dispatch-paper__close',
    '.fuel-plan-editor__header > button',
    '.route-editor__header > button',
    '.fleet-map-inspector__close',
  ]) {
    const block = [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)]
      .filter(([, selectors]) => selectors.trim() === selector)
      .map(([, , declarations]) => declarations)
      .join('\n');
    assert.ok(block.includes('width: var(--size-control-compact);'), selector);
    assert.ok(block.includes('padding: 0;'), selector);
  }
});
