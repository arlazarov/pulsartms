import assert from 'node:assert/strict';
import { resolve } from 'node:path';

// A phone's truck panel is open whole (no Details, owner decision of
// 2026-09-28) and is one fixed box over the map: the map's width and half
// its height (the owner, September 28). It scrolls up and down inside
// itself and never sideways, and reads the next stop, the facts and the
// clocks in that order down to the last clock.
// Failures go to check, so one run reports every broken variant; without
// it the first one throws.
export async function checkMobileTruckScrolling(
  page,
  output,
  name,
  check = (condition, message) => assert.ok(condition, message),
) {
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
      const part = selector => {
        const node = element.querySelector(selector);
        return node && node.getClientRects().length > 0
          ? node.getBoundingClientRect()
          : null;
      };
      const next = part('.fleet-truck-next'),
        facts = part('.fleet-truck-facts'),
        clocks = part('.fleet-truck-clocks');
      return {
        height: bounds.height,
        width: bounds.width,
        toggles: element.querySelectorAll('.fleet-map-mobile-summary__toggle')
          .length,
        map: { x: map.x, y: map.y, width: map.width, height: map.height },
        available: map.bottom - bounds.top,
        clientHeight: element.clientHeight,
        scrollHeight: element.scrollHeight,
        overflow: style.overflowY,
        // Read in order: the next stop, then the facts, then the clocks.
        order:
          !!next &&
          !!facts &&
          !!clocks &&
          next.bottom <= facts.top + 1 &&
          facts.bottom <= clocks.top + 1,
        // Whether the panel actually scrolls sideways, which is what a
        // reader complains about; a box that clips its own text to an
        // ellipsis has a scroll width and cannot be scrolled.
        horizontalOverflow: (() => {
          const start = element.scrollLeft;
          element.scrollLeft = element.scrollWidth;
          const moved = element.scrollLeft > 0;
          element.scrollLeft = start;
          return moved;
        })(),
        clientWidth: element.clientWidth,
        // Name what reaches past the panel's edge, so a failure says which
        // part is too wide.
        past: (() => {
          const edge = bounds.left + element.clientLeft + element.clientWidth;
          let worst = null;
          for (const node of element.querySelectorAll('*')) {
            const by = node.getBoundingClientRect().right - edge;
            if (by > 1 && by > (worst?.by ?? 0))
              worst = { name: node.className, by: Math.round(by) };
          }
          return worst;
        })(),
        withinMap: bounds.top >= map.top - 1 && bounds.bottom <= map.bottom + 1,
      };
    });
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
      const state = `${name}-${variant.label}`;
      const before = await geometry();
      await page.screenshot({
        path: resolve(output, `${name}-${variant.label}.png`),
      });
      check(
        ['auto', 'scroll'].includes(before.overflow),
        `${state}: overflow must remain reachable`,
      );
      check(
        !before.horizontalOverflow,
        `${state}: the phone panel scrolls sideways ` +
          `(${JSON.stringify(before.past)} in ${before.clientWidth}px)`,
      );
      check(
        before.past === null,
        `${state}: part of the phone panel reaches past its edge: ` +
          JSON.stringify(before.past),
      );
      check(before.withinMap, `${state}: panel leaves the map`);
      // The phone box: the map's width and half its height, whatever it
      // holds (the owner, September 28).
      check(
        Math.abs(before.width - before.map.width) <= 1 &&
          Math.abs(before.height - before.map.height / 2) <= 1,
        `${state}: the panel is the phone box, the map's width and half ` +
          `its height: ${JSON.stringify({
            width: before.width,
            height: before.height,
            map: before.map,
          })}`,
      );
      check(
        before.toggles === 0 &&
          (await inspector.locator('.fleet-truck-next').isVisible()) &&
          (await inspector.locator('.fleet-truck-facts').isVisible()) &&
          (await inspector.locator('.fleet-truck-clocks').isVisible()),
        `${state}: the panel is open whole, with no Details`,
      );
      check(
        before.order,
        `${state}: the next stop, the facts and the clocks read in order`,
      );
      await inspector.evaluate(element => {
        element.scrollTop = element.scrollHeight;
      });
      const end = await inspector.evaluate(element => ({
        top: element.scrollTop,
        maximum: Math.max(0, element.scrollHeight - element.clientHeight),
        last: [...element.querySelectorAll('.driver-hours__clock')]
          .at(-1)
          .getBoundingClientRect().bottom,
        bottom: element.getBoundingClientRect().bottom,
      }));
      check(
        Math.abs(end.top - end.maximum) <= 1,
        `${state}: scrolling reaches the end whenever content overflows`,
      );
      check(
        end.last <= end.bottom + 1,
        `${state}: the last clock remains reachable inside the panel`,
      );
      if (before.scrollHeight <= before.clientHeight)
        check(end.top === 0, `${state}: fitting content needs no scroll`);
      await page.screenshot({
        path: resolve(output, `${name}-${variant.label}-end.png`),
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
