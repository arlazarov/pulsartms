import assert from 'node:assert/strict';
import { resolve } from 'node:path';

export async function checkTruckReadingsLayout(page, output, name) {
  const originalViewport = page.viewportSize();
  const originalFont = await page.evaluate(() => ({
    value: document.documentElement.style.getPropertyValue('font-size'),
    priority: document.documentElement.style.getPropertyPriority('font-size'),
  }));
  const readings = page.locator('.fleet-map-truck-info');
  const head = page.locator('.fleet-map-inspector__hours');
  const geometry = () =>
    readings.evaluate(element => {
      const rect = node => {
        const { x, y, width, height, right, bottom } =
          node.getBoundingClientRect();
        const name =
          typeof node.className === 'string'
            ? node.className
            : (node.className?.baseVal ?? node.tagName);
        return { name, x, y, width, height, right, bottom };
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
        location: rect(
          element.querySelector('.fleet-map-truck-info__location'),
        ),
        // A reading is a quiet word and its value; only the weather keeps
        // an icon, because there the icon is the reading.
        icons: [
          ...element.querySelectorAll(
            '.fleet-map-truck-info__reading > small > svg, .fuel-reading__icon',
          ),
        ]
          .map(rect)
          .filter(box => box.width > 0),
        weather: [
          ...element.querySelectorAll('.fleet-map-truck-info__outside svg'),
        ]
          .map(rect)
          .filter(box => box.width > 0),
        // The clocks read in the card's head, as text; the vehicle line
        // below carries only what the truck itself is doing.
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
        // Which part of the line is too wide, rather than only that one is.
        tooWide: [...element.querySelectorAll('*')]
          .filter(
            node =>
              // A visually hidden label is a one-pixel box holding real
              // text; it is clipped on purpose and widens nothing.
              node.clientWidth > 2 &&
              node.scrollWidth - node.clientWidth > 1 &&
              ![...node.querySelectorAll('*')].some(
                child => child.scrollWidth - child.clientWidth > 1,
              ),
          )
          .map(node => ({
            name: node.className || node.tagName,
            by: Math.round(node.scrollWidth - node.clientWidth),
          })),
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
        assert.equal(
          g.dials.length,
          0,
          `${variant}: the vehicle line draws no clocks of its own`,
        );
        assert.equal(
          g.icons.length,
          0,
          `${variant}: a reading draws no icon of its own`,
        );
        assert.equal(
          g.weather.length,
          1,
          `${variant}: the weather keeps the icon that is its reading`,
        );
        for (const box of [...g.readings, ...g.weather]) {
          assert.ok(
            box.x >= g.left - 1 && box.right <= g.right + 1,
            `${variant}: readings and clocks stay within the content: ` +
              JSON.stringify({ box, left: g.left, right: g.right }),
          );
        }
        const clocks = await head.evaluate(element => {
          const box = node => {
            const { x, y, width, right, bottom } = node.getBoundingClientRect();
            return { x, y, width, right, bottom };
          };
          const style = getComputedStyle(element);
          const bounds = box(element);
          return {
            left: bounds.x + parseFloat(style.paddingLeft),
            right: bounds.right - parseFloat(style.paddingRight),
            dials: element.querySelectorAll('.driver-hours__dial').length,
            readings: [...element.querySelectorAll('.driver-hours__clock')].map(
              box,
            ),
          };
        });
        assert.equal(
          clocks.readings.length,
          4,
          `${variant}: the head reads four clocks`,
        );
        assert.equal(
          clocks.dials,
          0,
          `${variant}: the head's clocks are text, not dials`,
        );
        for (const clock of clocks.readings)
          assert.ok(
            clock.x >= clocks.left - 1 && clock.right <= clocks.right + 1,
            `${variant}: a clock leaves the head's content: ` +
              JSON.stringify({ clock, left: clocks.left, right: clocks.right }),
          );
        assert.ok(
          Math.abs(g.weather[0].width - g.weather[0].height) <= 1,
          `${variant}: the weather icon stays square`,
        );
        if (font === '100%') {
          if (g.columns === 2) {
            assert.ok(
              Math.abs(g.readings[0].y - g.readings[1].y) <= 1 &&
                g.readings[2].y >= g.readings[0].bottom,
              `${variant}: telemetry uses a two-by-two left group`,
            );
          }
          // A narrow card lets the clocks wrap; what it must not do is
          // fold them into a tower, which is what the one-column rule was
          // written to stop.
          const rows = new Set(clocks.readings.map(box => Math.round(box.y)));
          assert.ok(
            rows.size <= 2,
            `${variant}: the clocks read in one or two rows, not a tower ` +
              `(${rows.size})`,
          );
        }
        assert.ok(
          g.columns === 2
            ? g.location.x >= g.telemetry.right - 1 &&
                Math.abs(g.location.y - g.telemetry.y) <= 1
            : g.location.y >= g.telemetry.bottom - 1,
          `${variant}: the address ends the vehicle's line, or drops below it`,
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
