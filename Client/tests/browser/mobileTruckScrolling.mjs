import assert from 'node:assert/strict';
import { resolve } from 'node:path';

export async function checkMobileTruckScrolling(page, output, name) {
  const inspector = page.locator('.fleet-map-inspector');
  const originalViewport = page.viewportSize();
  const originalFont = await page.evaluate(() => ({
    value: document.documentElement.style.getPropertyValue('font-size'),
    priority: document.documentElement.style.getPropertyPriority('font-size'),
  }));
  const geometry = () =>
    inspector.evaluate(element => {
      const style = getComputedStyle(element);
      const bounds = element.getBoundingClientRect();
      const map = document.querySelector('#fleet-map').getBoundingClientRect();
      const main = element.querySelector('.fleet-map-route-details');
      const readings = element.querySelector('#fleet-map-telemetry-details');
      const contentBottom =
        main.getBoundingClientRect().bottom + element.scrollTop;
      return {
        height: bounds.height,
        contentHeight: contentBottom - bounds.top,
        maximumHeight: map.height * 0.6,
        available: map.bottom - bounds.top,
        clientHeight: element.clientHeight,
        scrollHeight: element.scrollHeight,
        scrollbar: style.scrollbarWidth,
        webkitScrollbar: getComputedStyle(element, '::-webkit-scrollbar')
          .display,
        overflow: style.overflowY,
        readingsBeforeLoad:
          readings.getBoundingClientRect().bottom <=
          main.getBoundingClientRect().top + 1,
        horizontalOverflow: element.scrollWidth > element.clientWidth + 1,
        withinMap: bounds.top >= map.top - 1 && bounds.bottom <= map.bottom + 1,
        map: { x: map.x, y: map.y, width: map.width, height: map.height },
      };
    });
  assert.equal(await page.locator('.fleet-map-truck-info__more').count(), 0);
  const variants = [
    { label: 'portrait', height: originalViewport.height, font: '100%' },
    { label: 'short', height: 600, font: '100%' },
    { label: 'large-text', height: originalViewport.height, font: '200%' },
  ];
  try {
    for (const variant of variants) {
      await page.setViewportSize({
        ...originalViewport,
        height: variant.height,
      });
      await page.evaluate(font => {
        document.documentElement.style.setProperty(
          'font-size',
          font,
          'important',
        );
      }, variant.font);
      await inspector.evaluate(element => {
        element.scrollTop = 0;
      });
      const before = await geometry();
      await page.screenshot({
        path: resolve(output, `${name}-${variant.label}-readings.png`),
      });
      assert.equal(before.scrollbar, 'auto');
      assert.equal(before.overflow, 'auto', 'Overflow must remain reachable');
      assert.equal(before.horizontalOverflow, false);
      assert.equal(before.withinMap, true);
      assert.equal(
        before.readingsBeforeLoad,
        true,
        'Telemetry and HOS appear before the load in normal flow',
      );
      assert.ok(
        Math.abs(
          before.height -
            Math.min(
              before.contentHeight,
              before.available,
              before.maximumHeight,
            ),
        ) <= 2,
        'The phone card fits its content up to 60% of the map height',
      );
      assert.equal(
        await page.locator('#fleet-map-route-details').isVisible(),
        true,
      );
      assert.equal(
        await page.locator('#fleet-map-truck-location').isVisible(),
        true,
      );
      if (variant.label === 'portrait') {
        const groups = await page
          .locator('.fleet-map-route-info')
          .evaluate(element => {
            const box = suffix =>
              element
                .querySelector(`.fleet-map-route-info__${suffix}`)
                .getBoundingClientRect();
            const load = box('load'),
              distance = box('distances');
            const visit = box('visit'),
              timing = box('timing');
            return {
              sameRow: Math.abs(load.top - distance.top) <= 1,
              arrivalSameRow: Math.abs(timing.top - visit.top) <= 1,
              columnsAligned: Math.abs(distance.left - timing.left) <= 1,
              timingGap: timing.left - visit.right,
            };
          });
        assert.ok(groups.sameRow, 'Load and Remaining share one group');
        assert.ok(groups.arrivalSameRow, 'Arrival facts sit beside the stop');
        assert.ok(groups.columnsAligned, 'Remaining aligns with Pickup');
        assert.ok(
          groups.timingGap >= 0 && groups.timingGap <= 24,
          'Stop address and arrival facts stay adjacent',
        );
        await inspector.evaluate(element => {
          element.scrollTop = element.scrollHeight;
        });
        await page.screenshot({
          path: resolve(output, `${name}-grouped-details.png`),
        });
      }
      await inspector.evaluate(element => {
        element.scrollTop = element.scrollHeight;
      });
      const end = await inspector.evaluate(element => ({
        top: element.scrollTop,
        maximum: Math.max(0, element.scrollHeight - element.clientHeight),
        routeBottom: element
          .querySelector('#fleet-map-route-details')
          .getBoundingClientRect().bottom,
        bottom: element.getBoundingClientRect().bottom,
      }));
      assert.ok(
        Math.abs(end.top - end.maximum) <= 1,
        'Scrolling reaches the end whenever content overflows',
      );
      assert.ok(
        end.routeBottom <= end.bottom + 1,
        'The final load facts remain reachable inside the card',
      );
      if (before.scrollHeight <= before.clientHeight)
        assert.equal(end.top, 0, 'Fitting content needs no scrolling');
      await page.screenshot({
        path: resolve(output, `${name}-${variant.label}-grouped-details.png`),
      });
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
