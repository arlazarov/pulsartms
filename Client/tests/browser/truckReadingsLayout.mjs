import assert from 'node:assert/strict';
import { resolve } from 'node:path';

export async function checkTruckReadingsLayout(page, output, name) {
  const originalViewport = page.viewportSize();
  const originalFont = await page.evaluate(() => ({
    value: document.documentElement.style.getPropertyValue('font-size'),
    priority: document.documentElement.style.getPropertyPriority('font-size'),
  }));
  const readings = page.locator('.fleet-map-truck-info');
  const geometry = () =>
    readings.evaluate(element => {
      const rect = node => {
        const { x, y, width, height, right, bottom } =
          node.getBoundingClientRect();
        return { x, y, width, height, right, bottom };
      };
      const style = getComputedStyle(element);
      const bounds = rect(element);
      const left = bounds.x + parseFloat(style.paddingLeft);
      const right = bounds.right - parseFloat(style.paddingRight);
      return {
        left,
        right,
        available: right - left,
        gap: parseFloat(style.columnGap),
        columns: style.gridTemplateColumns.split(' ').length,
        telemetry: rect(
          element.querySelector('.fleet-map-truck-info__telemetry'),
        ),
        hours: rect(element.querySelector('.driver-hours-panel')),
        icons: [
          ...element.querySelectorAll(
            '.fleet-map-truck-info__reading > small > svg, .fuel-reading__icon',
          ),
        ].map(rect),
        dials: [...element.querySelectorAll('.driver-hours__dial')].map(rect),
        readings: [
          ...element.querySelectorAll('.fleet-map-truck-info__reading'),
        ].map(rect),
        labels: [
          ...element.querySelectorAll(
            '.fleet-map-truck-info__reading > small > span, ' +
              '.fuel-reading__label',
          ),
        ].map(rect),
        overflow: element.scrollWidth > element.clientWidth + 1,
        scale:
          parseFloat(getComputedStyle(document.documentElement).fontSize) / 16,
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
        const g = await geometry();
        const variant = `${name}-${width}-${font}-readings`;
        assert.equal(
          g.overflow,
          false,
          `${variant}: no horizontal overflow: ${JSON.stringify(g)}`,
        );
        assert.equal(g.readings.length, 3);
        assert.equal(g.dials.length, 4);
        assert.equal(g.icons.length, 3);
        for (const box of [...g.readings, ...g.dials, ...g.icons]) {
          assert.ok(
            box.x >= g.left - 1 && box.right <= g.right + 1,
            `${variant}: readings and clocks stay within the content: ` +
              JSON.stringify({ box, left: g.left, right: g.right }),
          );
        }
        for (const dial of g.dials) {
          const expected = Math.min(
            58 * g.scale,
            (g.hours.width - 24 * g.scale) / 4,
          );
          assert.ok(
            Math.abs(dial.width - expected) <= 1,
            `${variant}: HOS dials fit their available column`,
          );
        }
        for (const icon of g.icons) {
          assert.ok(
            Math.abs(icon.width - 32 * g.scale) <= 1,
            `${variant}: telemetry icons retain their shared size`,
          );
        }
        if (font === '100%') {
          if (g.columns === 2) {
            assert.ok(
              Math.abs(g.readings[0].y - g.readings[1].y) <= 1 &&
                g.readings[2].y >= g.readings[0].bottom,
              `${variant}: telemetry uses a two-by-two left group`,
            );
          }
          assert.ok(
            g.dials.every(box => Math.abs(box.y - g.dials[0].y) <= 1),
            `${variant}: HOS uses one row`,
          );
          for (const row of [g.dials]) {
            for (let index = 1; index < row.length; index++) {
              const gap = row[index].x - row[index - 1].right;
              assert.ok(
                gap >= 7,
                `${variant}: row items keep at least the shared gap`,
              );
            }
          }
        }
        assert.ok(
          g.columns === 2
            ? g.hours.x >= g.telemetry.right &&
                Math.abs(g.hours.y - g.telemetry.y) <= 1
            : g.hours.y >= g.telemetry.bottom,
          `${variant}: HOS stays right, or stacks for enlarged text`,
        );
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
