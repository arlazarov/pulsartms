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
      // Whatever comes last in the card, and the card's own padding under
      // it: the route no longer closes the card since the vehicle line
      // moved onto the summary.
      const contentBottom =
        Math.max(
          ...[...element.children].map(
            child => child.getBoundingClientRect().bottom,
          ),
        ) +
        parseFloat(style.paddingBottom) +
        parseFloat(style.borderBottomWidth) +
        element.scrollTop;
      // The card's own cap, read from its stylesheet (half the stage on a
      // phone, the approved card of September 26) against the stage it is
      // a percentage of, rather than a number written here.
      const cap = style.maxHeight.endsWith('%')
        ? (parseFloat(style.maxHeight) / 100) *
          element.parentElement.getBoundingClientRect().height
        : parseFloat(style.maxHeight);
      return {
        height: bounds.height,
        contentHeight: contentBottom - bounds.top,
        maximumHeight: cap,
        maxHeight: style.maxHeight,
        // The phone contract, independent of the stylesheet: the card
        // takes at most half of the map it floats over.
        halfMap: map.height / 2,
        available: map.bottom - bounds.top,
        clientHeight: element.clientHeight,
        scrollHeight: element.scrollHeight,
        scrollbar: style.scrollbarWidth,
        webkitScrollbar: getComputedStyle(element, '::-webkit-scrollbar')
          .display,
        overflow: style.overflowY,
        readingsBeforeRoute:
          readings.getBoundingClientRect().bottom <=
          main.getBoundingClientRect().top + 1,
        // Whether the card actually scrolls sideways, which is the thing a
        // reader complains about. A scroll width past the client width is
        // not that on its own: a box that clips its own text to an ellipsis
        // has one and cannot be scrolled.
        horizontalOverflow: (() => {
          const start = element.scrollLeft;
          element.scrollLeft = element.scrollWidth;
          const moved = element.scrollLeft > 0;
          element.scrollLeft = start;
          return moved;
        })(),
        clientWidth: element.clientWidth,
        scrollWidth: element.scrollWidth,
        // Name what sticks out, so a sideways scroll says which part of the
        // card is too wide instead of only that one is. Two ways to be too
        // wide: reaching past the card's edge, or being a box whose own
        // contents do not fit it.
        overflowing: (() => {
          const edge =
            element.getBoundingClientRect().left + element.clientWidth;
          const worst = {
            over: Math.round(element.scrollWidth - element.clientWidth),
            past: null,
            wider: null,
          };
          for (const node of element.querySelectorAll('*')) {
            const past = node.getBoundingClientRect().right - edge;
            if (past > 1 && past > (worst.past?.by ?? 0))
              worst.past = { name: node.className, by: Math.round(past) };
          }
          // The innermost boxes that do not fit their own contents: an
          // ancestor is wide only because one of these is.
          worst.wider = [...element.querySelectorAll('*')]
            .filter(
              node =>
                // A visually hidden label (a 1px clipped box) always holds
                // more than it shows; it is not what makes a card wide.
                getComputedStyle(node).clipPath !== 'inset(50%)' &&
                node.scrollWidth - node.clientWidth > 1 &&
                ![...node.querySelectorAll('*')].some(
                  child => child.scrollWidth - child.clientWidth > 1,
                ),
            )
            .map(node => ({
              name: node.className || node.tagName,
              parent: node.parentElement?.className,
              text: (node.textContent ?? '').trim().slice(0, 40),
              by: Math.round(node.scrollWidth - node.clientWidth),
            }));
          return worst;
        })(),
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
      assert.equal(
        before.horizontalOverflow,
        false,
        `The phone card scrolls sideways at ${variant.label}/${variant.font}: ` +
          `${JSON.stringify(before.overflowing)} in ${before.clientWidth}px`,
      );
      assert.equal(
        before.overflowing.past,
        null,
        `Part of the phone card reaches past its edge at ` +
          `${variant.label}/${variant.font}: ` +
          `${JSON.stringify(before.overflowing.past)}`,
      );
      assert.equal(before.withinMap, true);
      assert.equal(
        before.readingsBeforeRoute,
        true,
        'The vehicle line sits on the summary, above the route',
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
        `The phone card fits its content up to its own cap ` +
          `(${before.maxHeight} of the stage) at ${variant.label}: ` +
          `${JSON.stringify({
            height: before.height,
            content: before.contentHeight,
            available: before.available,
            maximum: before.maximumHeight,
          })}`,
      );
      assert.ok(
        before.height <= before.halfMap + 1,
        `The phone card stays within half of the map at ${variant.label}: ` +
          `${before.height} of ${before.map.height}`,
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
            // The load, the miles left and the arrival read in the card
            // head; the route carries the visit (the next stop, then what
            // is left of the load and where the truck is) and the facts
            // column of actions (the approved card of September 26).
            const visit = box('visit'),
              next = box('next'),
              where = box('where'),
              facts = box('facts');
            return {
              // A phone card is narrower than the two-column threshold, so
              // the groups stack in the order they are read.
              arrivalBelowStop:
                where.top >= next.bottom - 1 && facts.top >= visit.bottom - 1,
              columnsAligned:
                Math.abs(next.left - visit.left) <= 1 &&
                Math.abs(where.left - visit.left) <= 1 &&
                Math.abs(facts.left - visit.left) <= 1,
              sameWidth: Math.abs(visit.width - facts.width) <= 1,
            };
          });
        assert.ok(
          groups.arrivalBelowStop,
          'A stacked card puts what is left under the stop and the actions ' +
            'under both',
        );
        assert.ok(groups.columnsAligned, 'Stacked groups share one left edge');
        assert.ok(groups.sameWidth, 'Stacked groups fill the same width');
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
