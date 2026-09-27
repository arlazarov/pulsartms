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
        telemetry: rect(element.querySelector('.truck-readings')),
        location: rect(
          document.querySelector('.fleet-map-inspector__location'),
        ),
        // Every reading leads with its icon (the approved card of
        // September 26); the weather's icon is its reading and is counted
        // on its own. The weather is a cell of the same shared line
        // (TruckReadings), so the provider readings exclude it.
        icons: [
          ...element.querySelectorAll(
            '.truck-readings__reading:not(.truck-readings__reading--outside)' +
              ' > small > svg, .fuel-reading__icon',
          ),
        ]
          .map(rect)
          .filter(box => box.width > 0),
        weather: [
          ...element.querySelectorAll('.truck-readings__reading--outside svg'),
        ]
          .map(rect)
          .filter(box => box.width > 0),
        // The clocks read in the card's head, as text; the vehicle line
        // below carries only what the truck itself is doing.
        dials: [...element.querySelectorAll('.driver-hours__dial')].map(rect),
        readings: [
          ...element.querySelectorAll(
            '.truck-readings__reading:not(.truck-readings__reading--outside)',
          ),
        ].map(rect),
        labels: [
          ...element.querySelectorAll(
            '.truck-readings__reading:not(.truck-readings__reading--outside)' +
              ' > small > span, ' +
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
        cardScroll: element.closest('.fleet-map-inspector').scrollTop,
        headPosition: getComputedStyle(
          element.closest('.fleet-map-inspector__header'),
        ).position,
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
        // A card at least map-compact-columns (40rem) wide has a closed
        // state, with where the truck is behind Details; open it, as a
        // dispatcher would, before reading the lower rows. A narrower card
        // is open whole and shows no disclosure.
        const toggle = page.locator('.fleet-map-mobile-summary__toggle');
        if (
          (await toggle.isVisible()) &&
          (await toggle.getAttribute('aria-expanded')) === 'false'
        )
          await toggle.click();
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
          3,
          `${variant}: speed, fuel and engine each lead with one icon`,
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
          // The block holding the clocks lays nothing out; its rows are the
          // header's, so the header's content box is the edge to keep to.
          const frame = element.closest('.fleet-map-inspector__header');
          const style = getComputedStyle(frame);
          const bounds = box(frame);
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
          g.location.y >= g.telemetry.bottom - 1,
          `${variant}: the address is last, below the vehicle's line ` +
            JSON.stringify({
              location: g.location,
              telemetry: g.telemetry,
              cardScroll: g.cardScroll,
              headPosition: g.headPosition,
            }),
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
