// Interaction checks for the concept with the labelled demo fixtures.
// Drives headless Chrome over the DevTools protocol (no extra packages).
//   node prototypes/futuristic-ui/check.mjs [profileDir]
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { launch, sleep } from './cdp.mjs';

const base = process.env.CONCEPT_URL ?? 'http://localhost:5179';
const profile = process.argv[2] ?? mkdtempSync(join(tmpdir(), 'concept-'));
let b;
const send = (...a) => b.send(...a);
const evaluate = (e) => b.evaluate(e);

const results = [];
function check(name, ok, detail) {
  results.push({ name, ok, detail });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? `  (${detail})` : ''}`);
}

try {
  b = await launch(profile);
  await b.viewport(1600, 940);
  const truck = '00000000-0000-4000-8000-000000001000';
  await send('Page.navigate', {
    url: `${base}/?fixtures=1&theme=dark#/fleet?truck=${truck}`,
  });
  await sleep(3500);

  const pos = `const g = document.querySelector('.truck.selected');
    const r = g.getBoundingClientRect();
    return [Math.round(r.x + r.width / 2), Math.round(r.y + r.height / 2)];`;

  // Selection agrees across list, map, detail panel and chain.
  const agree = await evaluate(`
    return {
      row: document.querySelector('tr[aria-selected="true"] .unit')
        ?.textContent.trim(),
      marker: document.querySelector('.truck.selected')
        ?.getAttribute('aria-label'),
      panel: document.querySelector('.truck-head h2')?.textContent,
      chain: document.querySelector('.chain-label b')?.textContent,
    };`);
  check('Truck selection agrees in list, map, panel and chain',
    agree.row === 'D-101' && agree.marker?.startsWith('Truck D-101') &&
      agree.panel === 'D-101' && agree.chain === 'Truck D-101',
    JSON.stringify(agree));

  // Chain click selects the trip in the panel and focuses its route.
  const trip = await evaluate(`
    document.querySelectorAll('.link-card')[2].click();
    await new Promise((r) => setTimeout(r, 400));
    return {
      chain: document.querySelector('.link[data-selected="true"] .load')
        ?.textContent,
      panel: document.querySelector('.trip[data-selected="true"] .load')
        ?.textContent,
      open: document.querySelector('.trip[data-open="true"] .load')
        ?.textContent,
      focus: document.querySelectorAll('.route.focus').length,
    };`);
  check('Chain trip selects the same trip in panel and map',
    trip.chain === trip.panel && trip.panel === trip.open && trip.focus > 0,
    JSON.stringify(trip));

  // Follow: start, survive two position polls, keep the anchor.
  const start = await evaluate(`
    document.querySelector('[data-action=follow]').click();
    await new Promise((r) => setTimeout(r, 500));
    const s = await import('/js/store.js');
    return { following: s.state.following,
      pressed: document.querySelector('[data-action=follow]')
        .getAttribute('aria-pressed'),
      pos: (() => { ${pos} })(),
      lat: s.state.feeds.locations.data.trucks[0].latitude };`);
  check('Follow starts and shows pressed state',
    start.following && start.pressed === 'true', JSON.stringify(start));
  await sleep(21000);
  const after = await evaluate(`
    const s = await import('/js/store.js');
    return { following: s.state.following,
      pos: (() => { ${pos} })(),
      lat: s.state.feeds.locations.data.trucks[0].latitude };`);
  const drift = Math.hypot(after.pos[0] - start.pos[0],
    after.pos[1] - start.pos[1]);
  check('Follow keeps the truck on its anchor as positions update',
    after.following && after.lat !== start.lat && drift <= 2,
    `lat ${start.lat.toFixed(5)} -> ${after.lat.toFixed(5)}, drift ${
      drift.toFixed(1)}px`);

  // A user drag ends Follow.
  const drag = await evaluate(`
    const svg = document.querySelector('.map-svg');
    const r = svg.getBoundingClientRect();
    const o = { bubbles: true, pointerId: 7, clientX: r.x + 200,
      clientY: r.y + 200, isPrimary: true };
    svg.dispatchEvent(new PointerEvent('pointerdown', o));
    svg.dispatchEvent(new PointerEvent('pointermove', { ...o,
      clientX: r.x + 260, clientY: r.y + 230 }));
    svg.dispatchEvent(new PointerEvent('pointerup', { ...o,
      clientX: r.x + 260, clientY: r.y + 230 }));
    await new Promise((r) => setTimeout(r, 300));
    const s = await import('/js/store.js');
    return s.state.following;`);
  check('Dragging the map ends Follow', drag === false);

  // Follow again, then select another truck: Follow ends.
  const sw = await evaluate(`
    document.querySelector('[data-action=follow]').click();
    await new Promise((r) => setTimeout(r, 300));
    const s = await import('/js/store.js');
    const before = s.state.following;
    document.querySelectorAll('tr[data-action="truck"]')[1].click();
    await new Promise((r) => setTimeout(r, 600));
    return { before, after: s.state.following,
      panel: document.querySelector('.truck-head h2')?.textContent };`);
  check('Selecting another truck ends Follow and switches the panel',
    sw.before && !sw.after && sw.panel === 'D-102', JSON.stringify(sw));

  // Fit route ends Follow.
  const fit = await evaluate(`
    document.querySelector('[data-action=follow]').click();
    await new Promise((r) => setTimeout(r, 300));
    document.querySelector('.truck-actions [data-action=fit-route]').click();
    await new Promise((r) => setTimeout(r, 300));
    const s = await import('/js/store.js');
    return s.state.following;`);
  check('Fit route ends Follow', fit === false);

  // Filters, search, tabs, panels and theme.
  const misc = await evaluate(`
    const q = (s) => document.querySelector(s);
    q('[data-action=filter][data-value=stopped]').click();
    await new Promise((r) => setTimeout(r, 300));
    const stopped = document.querySelectorAll('tr[data-action=truck]').length;
    q('[data-action=filter][data-value=all]').click();
    const input = q('[data-region=search] input');
    input.value = 'DEMO-9403';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await new Promise((r) => setTimeout(r, 300));
    const hits = document.querySelectorAll('.results li').length;
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter',
      bubbles: true }));
    await new Promise((r) => setTimeout(r, 500));
    const picked = q('.link[data-selected="true"] .load')?.textContent;
    q('[data-action=tab][data-value=fuel]').click();
    await new Promise((r) => setTimeout(r, 300));
    const fuel = !!q('.fuel-gauge');
    q('[data-action=toggle-left]').click();
    await new Promise((r) => setTimeout(r, 300));
    const left = q('[data-region=ws]').dataset.left;
    q('[data-action=toggle-left]').click();
    const theme0 = document.documentElement.dataset.theme;
    q('.topbar [data-action=theme]').click();
    await new Promise((r) => setTimeout(r, 200));
    const theme1 = document.documentElement.dataset.theme;
    return { stopped, hits, picked, fuel, left, theme0, theme1 };`);
  check('Stopped filter narrows the list', misc.stopped === 6,
    `${misc.stopped} rows`);
  check('Search finds a load and selects its trip',
    misc.hits > 0 && misc.picked === 'DEMO-9403', JSON.stringify(misc));
  check('Fuel tab opens', misc.fuel);
  check('Left panel collapses', misc.left === 'closed');
  check('Theme switches', misc.theme0 !== misc.theme1);

  // Dispatch shares the selection.
  const disp = await evaluate(`
    location.hash = '#/dispatch';
    await new Promise((r) => setTimeout(r, 1200));
    return {
      lane: document.querySelector('.lane-row[data-selected="true"] .unit')
        ?.textContent.trim(),
      detail: document.querySelector('[data-region=right] h2')?.textContent,
      chain: document.querySelector('.chain-label b')?.textContent,
      pollsAlive: typeof window !== 'undefined',
    };`);
  check('Dispatch keeps the Fleet selection',
    disp.lane === 'D-101' && disp.detail === 'DEMO-9403' &&
      disp.chain === 'Truck D-101', JSON.stringify(disp));

  // Page overflow at phone widths.
  for (const w of [390, 360]) {
    await b.viewport(w, 844, true);
    for (const route of ['fleet', 'dispatch']) {
      const o = await evaluate(`
        location.hash = '#/${route}';
        await new Promise((r) => setTimeout(r, 900));
        return document.documentElement.scrollWidth -
          document.documentElement.clientWidth;`);
      check(`No page horizontal overflow · ${route} @ ${w}px`, o <= 0,
        `${o}px`);
    }
  }
} catch (e) {
  check('Check run completed', false, e.message);
} finally {
  b?.close();
  await sleep(300);
  if (!process.argv[2]) rmSync(profile, { recursive: true, force: true });
  const failed = results.filter((r) => !r.ok).length;
  console.log(`${results.length - failed}/${results.length} passed`);
  process.exitCode = failed ? 1 : 0;
}
