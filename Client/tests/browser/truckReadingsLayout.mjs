import assert from 'node:assert/strict';
import { resolve } from 'node:path';

// The truck panel's readings (the owner, September 27): eight facts in two
// columns, each led by one square icon, the location last, and the
// driver's four clocks as text cells, from 767px down to 320px at 100% and
// 200% text. Nothing scrolls sideways and nothing leaves its group.
// Failures go to check, so one run reports every broken variant; without
// it the first one throws.
export async function checkTruckReadingsLayout(
  page,
  output,
  name,
  check = (condition, message) => assert.ok(condition, message),
) {
  const originalViewport = page.viewportSize();
  const originalFont = await page.evaluate(() => ({
    value: document.documentElement.style.getPropertyValue('font-size'),
    priority: document.documentElement.style.getPropertyPriority('font-size'),
  }));
  const facts = page.locator('.fleet-truck-facts');
  const geometry = () =>
    facts.evaluate(element => {
      const box = node => {
        const { x, y, width, height, right, bottom } =
          node.getBoundingClientRect();
        return { x, y, width, height, right, bottom };
      };
      const style = getComputedStyle(element);
      const bounds = box(element);
      const clocks = element.parentElement.querySelector('.fleet-truck-clocks');
      const clocksBox = box(clocks);
      return {
        left: bounds.x + parseFloat(style.borderLeftWidth),
        right: bounds.right - parseFloat(style.borderRightWidth),
        columns: style.gridTemplateColumns.split(' ').length,
        overflow: element.scrollWidth > element.clientWidth + 1,
        panelOverflow:
          element.closest('.fleet-map-inspector').scrollWidth >
          element.closest('.fleet-map-inspector').clientWidth + 1,
        // Which part is too wide, rather than only that one is.
        tooWide: [...element.querySelectorAll('*')]
          .filter(
            node =>
              node.clientWidth > 2 &&
              getComputedStyle(node).textOverflow !== 'ellipsis' &&
              node.scrollWidth - node.clientWidth > 1 &&
              ![...node.querySelectorAll('*')].some(
                child => child.scrollWidth - child.clientWidth > 1,
              ),
          )
          .map(node => ({
            name: node.className || node.tagName,
            by: Math.round(node.scrollWidth - node.clientWidth),
          })),
        cells: [...element.querySelectorAll('.fleet-truck-facts__fact')].map(
          cell => ({
            name: cell.querySelector('dt').textContent.trim(),
            ...box(cell),
            icons: [...cell.querySelectorAll(':scope > svg')].map(box),
          }),
        ),
        clocksBox,
        dials: clocks.querySelectorAll('.driver-hours__dial').length,
        clocks: [...clocks.querySelectorAll('.driver-hours__clock')].map(box),
      };
    });
  try {
    for (const font of ['100%', '200%']) {
      await page.evaluate(value => {
        document.documentElement.style.setProperty(
          'font-size',
          value,
          'important',
        );
      }, font);
      for (const width of [767, 600, 520, 441, 390, 320]) {
        await page.setViewportSize({ ...originalViewport, width });
        // A phone's panel opens closed on the next stop; open it, as a
        // dispatcher would, before reading the facts. A wider one is whole.
        const toggle = page.locator(
          '.fleet-map-inspector[data-inspector-mode="truck"] ' +
            '.fleet-map-mobile-summary__toggle',
        );
        if (
          (await toggle.isVisible()) &&
          (await toggle.getAttribute('aria-expanded')) === 'false'
        )
          await toggle.click();
        const g = await geometry();
        const variant = `${name}-${width}-${font}-readings`;
        check(
          !g.overflow && !g.panelOverflow,
          `${variant}: no horizontal overflow: ${JSON.stringify(g.tooWide)}`,
        );
        check(
          JSON.stringify(g.cells.map(cell => cell.name)) ===
            JSON.stringify([
              'Driver',
              'Trailer',
              'Motion',
              'Duty',
              'Fuel',
              'Engine',
              'Temperature',
              'Location',
            ]),
          `${variant}: the eight facts in reading order`,
        );
        check(g.columns === 2, `${variant}: the facts keep two columns`);
        for (const cell of g.cells) {
          check(
            cell.x >= g.left - 1 && cell.right <= g.right + 1,
            `${variant}: ${cell.name} stays within the facts: ` +
              JSON.stringify({ cell, left: g.left, right: g.right }),
          );
          check(
            cell.icons.length === 1 &&
              cell.icons[0].width > 0 &&
              Math.abs(cell.icons[0].width - cell.icons[0].height) <= 1,
            `${variant}: ${cell.name} leads with one square icon`,
          );
        }
        const location = g.cells.at(-1);
        check(
          g.cells.slice(0, -1).every(cell => location.y >= cell.y - 1) &&
            location.y >= g.cells[5].bottom - 1,
          `${variant}: the location is the last row of facts`,
        );
        check(g.clocks.length === 4, `${variant}: the panel reads four clocks`);
        check(g.dials === 0, `${variant}: the clocks are text, not dials`);
        for (const clock of g.clocks)
          check(
            clock.x >= g.clocksBox.x - 1 &&
              clock.right <= g.clocksBox.right + 1,
            `${variant}: a clock leaves the clocks' row: ` +
              JSON.stringify({ clock, box: g.clocksBox }),
          );
        if (font === '100%') {
          // A narrow panel lets the clocks wrap; it must not fold them into
          // a tower.
          const rows = new Set(g.clocks.map(box => Math.round(box.y)));
          check(
            rows.size <= 2,
            `${variant}: the clocks read in one or two rows, not a tower ` +
              `(${rows.size})`,
          );
        }
        if (width === 390 || width === 441 || width === 600) {
          await page.screenshot({ path: resolve(output, `${variant}.png`) });
        }
        if (width === 390 && font === '100%') {
          await page.locator('#fleet-map').focus();
          await page.locator('.fleet-map-inspector').screenshot({
            path: resolve(output, `${name}-mobile-card.png`),
          });
        }
      }
    }
  } finally {
    await page.setViewportSize(originalViewport);
    await page.evaluate(saved => {
      const style = document.documentElement.style;
      if (saved.value) {
        style.setProperty('font-size', saved.value, saved.priority);
      } else {
        style.removeProperty('font-size');
      }
      document.querySelector('.fleet-map-inspector').scrollTop = 0;
    }, originalFont);
  }
}
