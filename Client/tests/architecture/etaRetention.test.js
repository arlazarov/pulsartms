import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

test('previous ETA retention is bounded, stop-scoped and limited to pending recalculation', () => {
  const source = readFileSync(
    new URL(
      '../../Shared/DriverStatus/ArrivalEstimate/ArrivalEstimate.razor.cs',
      import.meta.url,
    ),
    'utf8',
  );
  assert.match(source, /Memory.Update\(DispatchId, Stop, Eta, Completed\)/);
  const page = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
    'utf8',
  );
  // The truck panel's next stop line (owner, September 27: "Next stop line
  // at the top of the truck panel", which replaced the old card head) and
  // the route section each show the arrival, so each is given the same
  // dispatch, stop and pending flag - and its own memory, because one
  // memory updated twice per render would advance its "stop changed" state
  // twice for a single change.
  const estimates = page.match(/<ArrivalEstimate\b[\s\S]*?\/>/g) || [];
  assert.equal(
    estimates.length,
    2,
    'the next stop line and the route each retain one',
  );
  const memories = new Set();
  for (const estimate of estimates) {
    const memory = estimate.match(/Memory="(_\w+)"/)?.[1];
    assert.ok(memory, 'every estimate retains through a memory');
    memories.add(memory);
    assert.match(estimate, /DispatchId="SelectedDispatchId"/);
    assert.match(estimate, /Stop="ScheduledStop"/);
    assert.match(estimate, /Refreshing="_etaRefreshPending"/);
  }
  assert.equal(
    memories.size,
    estimates.length,
    'the estimates keep separate memories',
  );
});
