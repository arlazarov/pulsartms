import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

export function workspaceReadModel(load) {
  return {
    load,
    revision: 1,
    sourceFingerprint: `fixture-${load.id}`,
    metadata: {
      orderNumber: load.orderNumber,
      customerName: load.customerName,
      price: load.price,
      currency: load.currency,
      brokerCompany: load.customerName,
      loadInstructions: '',
    },
    stops: load.stops.map(stop => {
      const pickup = /pick/i.test(stop.job);
      const reference = pickup ? 'Shipper' : 'Receiver';
      const match = stop.notes?.match(
        new RegExp(`${reference} appointment confirmation number: (\\w+)`),
      );
      return {
        ...stop,
        segmentKey: 'protected-fixture-segment',
        canEdit: false,
        canMove: false,
        canRemove: false,
        lockReason: 'This stop belongs to a protected execution segment.',
        appointmentReference: match?.[1] ?? '',
        appointmentMode: stop.isWindow ? 'window' : 'at',
        timeZoneId: 'America/Toronto',
        truckNumber: load.truckNumber,
        trailerNumber: load.trailerNumber,
        driverName: load.driverName,
      };
    }),
    canEdit: true,
    locallyManaged: false,
    sourceName: 'TorqueAI',
    sourceUpdatedAt: new Date().toISOString(),
    history: [],
  };
}

export async function workspacePage(page) {
  const workspace = page.locator('.dispatch-details');
  await workspace.locator('h1').waitFor();
  await workspace
    .locator('.stop-workspace__stop')
    .first()
    .waitFor({ state: 'attached' });
  return workspace;
}

export async function workspaceStop(page, id) {
  const mobile = page.locator('.stop-workspace__mobile-tabs');
  const isMobile = await mobile.isVisible();
  if (isMobile)
    await mobile.getByRole('button', { name: 'Stops', exact: true }).click();
  const positions = () =>
    page.locator('.stop-workspace__stop').evaluateAll(rows =>
      rows.map(row => {
        const box = row.getBoundingClientRect();
        const list = row.closest('.stop-workspace__list');
        const bounds = list.getBoundingClientRect();
        return [
          box.x - bounds.x,
          box.y - bounds.y + list.scrollTop,
          box.width,
          box.height,
        ];
      }),
    );
  const before = await positions();
  const stop = page.locator(`.stop-workspace__stop[data-stop-id="${id}"]`);
  const select = stop.locator('.stop-workspace__select');
  const editor = page.locator(`#stop-editor-${id}`);
  if (isMobile || !(await editor.count())) await select.click();
  await editor.waitFor();
  assert.equal(await select.getAttribute('aria-pressed'), 'true');
  assert.equal(await stop.locator('.stop-workspace__editor').count(), 0);
  const after = await positions();
  assert.equal(after.length, before.length);
  if (!isMobile)
    assert.ok(
      after.every((row, index) =>
        row.every((value, axis) => Math.abs(value - before[index][axis]) <= 1),
      ),
      'Selecting a stop must not move or resize any itinerary row',
    );
  return editor;
}

export async function returnToDispatch(
  page,
  view = 'Cards',
  completed = false,
) {
  await page.getByRole('link', { name: '← Back to Dispatch' }).click();
  await page.locator('.filter-toolbar').waitFor();
  const scope = page.locator(
    completed ? '#dispatch-completed' : '#dispatch-active',
  );
  if ((await scope.getAttribute('aria-pressed')) !== 'true')
    await scope.click();
  const button = page.getByRole('button', { name: view, exact: true });
  if ((await button.getAttribute('aria-pressed')) !== 'true')
    await button.click();
  const ready = {
    Cards: '.dispatch-load',
    Table: '.dispatch-table tbody tr',
    Papers: '.dispatch-paper__tab',
  };
  await page.locator(ready[view]).first().waitFor();
}

export async function workspaceFinancials(
  page,
  check,
  name,
  load,
  capture = null,
) {
  const workspace = await workspacePage(page);
  const broker = workspace.locator('.dispatch-broker');
  const price = workspace.getByLabel('Load rate', { exact: true });
  const tabs = workspace.locator('.dispatch-details__tab');
  const labels = (await tabs.allTextContents()).map(label => label.trim());
  check(
    labels.join('|') === 'Broker & billing|Overview|History',
    name + ' broker leads section navigation without an Assignments tab',
  );
  const checkCurrentTab = async label => {
    const current = await workspace
      .locator('.dispatch-details__tab[aria-current="page"]')
      .allTextContents();
    check(
      current.length === 1 && current[0].trim() === label,
      `${name} exactly one current tab matches ${label}`,
    );
  };
  await checkCurrentTab('Overview');
  check(
    (await broker.isHidden()) && (await price.isHidden()),
    name + ' Overview keeps broker contacts and pricing in their own tab',
  );
  await workspace
    .getByRole('button', { name: 'Broker & billing', exact: true })
    .click();
  await checkCurrentTab('Broker & billing');
  check(
    (await broker.isVisible()) && (await price.isVisible()),
    name + ' Broker & billing shows contacts alongside the load rate',
  );
  check(
    (await broker
      .getByLabel('Broker company', { exact: true })
      .inputValue()) === load.customerName,
    name + ' broker identity remains attached to the selected load',
  );
  const financials = workspace.locator('.dispatch-details__financials');
  const expected = [
    ['Loaded distance', `${load.loadedMiles} mi`],
    ['Empty distance', `${load.emptyMiles} mi`],
    ['Total distance', `${load.totalMiles} mi`],
    ['Loaded RPM', `${load.loadedRatePerMile.toFixed(2)} ${load.currency}`],
    ['Total RPM', `${load.totalRatePerMile.toFixed(2)} ${load.currency}`],
  ];
  for (const [label, value] of expected) {
    const field = financials.locator('div').filter({ hasText: label });
    check(
      (await field.locator('dd').innerText())
        .replace(/\s+/g, ' ')
        .includes(value),
      `${name} saved server ${label} remains ${value}`,
    );
  }
  const rate = load.price.toLocaleString('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  check(
    (await workspace.locator('.dispatch-details__summary').innerText())
      .replace(/\s+/g, ' ')
      .includes(`${rate} ${load.currency}`),
    name + ' retains the server-provided load rate',
  );
  if (capture) {
    const label = name.replace(/[^a-z0-9_-]+/gi, '-');
    const image = resolve(
      capture.output,
      `${label}-load-${load.loadNumber}-broker-billing.png`,
    );
    const tabState = async () =>
      tabs.evaluateAll(elements =>
        elements.map(element => {
          const style = getComputedStyle(element);
          return {
            text: element.textContent.trim(),
            current: element.getAttribute('aria-current'),
            background: style.backgroundColor,
            color: style.color,
            hovered: element.matches(':hover'),
          };
        }),
      );
    const settleTabs = async () =>
      workspace.locator('.dispatch-details__tabs').evaluate(async element => {
        await Promise.all(
          element
            .getAnimations({ subtree: true })
            .map(animation => animation.finished.catch(() => {})),
        );
      });
    const immediate = await tabState();
    await settleTabs();
    const settledHover = await tabState();
    await page.mouse.move(0, 0);
    await settleTabs();
    const settledOutside = await tabState();
    await writeFile(
      image.replace(/\.png$/, '-tabs.json'),
      JSON.stringify(
        {
          expectedCurrent: 'Broker & billing',
          immediate,
          settledHover,
          settledOutside,
        },
        null,
        2,
      ),
    );
    await page.screenshot({ path: image, fullPage: true });
    capture.screenshots.push(image);
  }
  await workspace
    .getByRole('button', { name: 'Overview', exact: true })
    .click();
  await checkCurrentTab('Overview');
  check(
    (await broker.isHidden()) && (await price.isHidden()),
    name + ' returning to Overview hides broker and rate without a duplicate',
  );
}

export async function checkWorkspaceLoads(
  page,
  name,
  screenshots,
  { loads, check, output },
) {
  for (const [index, load] of loads.entries()) {
    const link = page
      .locator('.dispatch-load__footer .dispatch-load__details')
      .nth(index);
    await link.focus();
    await link.press('Enter');
    const workspace = await workspacePage(page);
    check(
      new URL(page.url()).pathname === `/dispatch/${load.id}` &&
        (await page.locator('dialog[open]').count()) === 0,
      name + ' keyboard opens the load as a full page, not a modal',
    );
    check(
      (await workspace.locator('h1').innerText()).includes(
        `AMF${load.loadNumber}`,
      ) &&
        (
          await workspace.locator('.dispatch-details__status').innerText()
        ).toLowerCase() === load.status.replaceAll('_', ' '),
      name + ' workspace retains the selected load identity and status',
    );
    await workspaceFinancials(page, check, name, load, {
      output,
      screenshots,
    });
    const items = workspace.locator('.stop-workspace__stop');
    check(
      (await items.count()) === load.stops.length,
      name + ' workspace retains every load stop',
    );
    const initial = resolve(output, `${name}-load-${index + 1}-workspace.png`);
    await page.screenshot({ path: initial, fullPage: true });
    screenshots.push(initial);
    for (const [position, expected] of load.stops.entries()) {
      const globalIndex = index === 0 ? position : position + 3;
      check(
        (await items.nth(position).getAttribute('data-stop-id')) ===
          expected.id,
        name + ' workspace preserves stop identity and order',
      );
      const stop = await workspaceStop(page, expected.id);
      check(
        (await workspace.locator('.stop-workspace__editor:visible').count()) ===
          1 &&
          (await workspace.locator('.stop-workspace__stop').count()) ===
            load.stops.length,
        name + ' selected editor retains the complete itinerary',
      );
      if (position === 0) {
        check(
          await stop.locator('.stop-operation--inline').isVisible(),
          name + ' resource and stop actions are available within Overview',
        );
        const operation = workspace.locator('.stop-operation');
        await operation
          .getByLabel('Type', { exact: true })
          .selectOption('Driver start');
        check(
          (await operation
            .getByLabel('Trailer after stop', { exact: true })
            .count()) === 0 &&
            (await operation.innerText()).includes('without a truck'),
          name +
            ' personal departure has its single No truck state without another selector',
        );
        check(
          await operation.evaluate(
            element => element.scrollWidth <= element.clientWidth + 2,
          ),
          name + ' operation controls fit the selected stop execution section',
        );
        const image = resolve(
          output,
          `${name}-load-${index + 1}-operation.png`,
        );
        await operation.scrollIntoViewIfNeeded();
        await page.screenshot({ path: image, fullPage: true });
        screenshots.push(image);
        await workspace
          .getByRole('button', { name: 'Discard', exact: true })
          .click();
      }
      // A transfer stop is read-only and renders its saved facts as text; an
      // ordinary stop is editable and holds the same facts in its fields.
      // Both must carry them, so read the editor's text together with the
      // values of its controls.
      const facts = (
        await stop.evaluate(editor =>
          [
            editor.innerText,
            ...[...editor.querySelectorAll('input, textarea, select')]
              .map(field => field.value)
              .filter(Boolean),
          ].join(' '),
        )
      ).replace(/\s+/g, ' ');
      const prefix = /pick/i.test(expected.job) ? 'PU' : 'DL';
      check(
        facts.includes(`Appt # ${prefix}${load.loadNumber}`) ||
          facts.includes(`${prefix}${load.loadNumber}`),
        name + ' workspace shows job-specific appointment reference',
      );
      check(
        facts.includes(expected.stopNo),
        name + ' workspace keeps the stop reference distinct from appointment',
      );
      if (globalIndex === 0) {
        check(
          (
            await page.locator(`[data-stop-id="${expected.id}"]`).innerText()
          ).includes('Completed') &&
            !(await stop
              .locator(
                '.stop-workspace__legacy-cycle, .arrival-estimate__timezone',
              )
              .count()),
          name + ' completed pickup has no stale forecast',
        );
        check(
          facts.includes('Refrigerated produce') &&
            facts.includes('43063 lbs') &&
            facts.includes('26') &&
            facts.includes('Food-grade trailer required'),
          name + ' protected pickup retains cargo and complete instructions',
        );
      } else {
        check(
          facts.includes('America/Toronto'),
          name + ' workspace retains the stop time zone',
        );
        check(
          (await stop.locator('.stop-workspace__legacy-cycle').count()) === 0,
          name + ' selected editor omits the removed duplicate cycle forecast',
        );
      }
      check(
        await stop.evaluate(element => {
          const box = element.getBoundingClientRect();
          return (
            box.left >= -1 &&
            box.right <= document.documentElement.clientWidth + 1 &&
            element.scrollWidth <= element.clientWidth + 2
          );
        }),
        name + ' selected stop remains horizontally contained in the page',
      );
      await workspaceStop(page, expected.id);
      check(
        (await workspace.locator('.stop-workspace__editor').count()) === 1,
        name + ' selecting the same stop retains the stable editor',
      );
    }
    await returnToDispatch(page);
  }
}
