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
  // at the top of the truck panel", which replaced the old card head and
  // its hidden route section) is the one reader of the arrival. The panel
  // stays mounted while a truck is selected, hidden in the other views, so
  // its memory retains the forecast on the way back; a second reader of
  // the same memory would advance its "stop changed" state twice for a
  // single change.
  const estimates = page.match(/<ArrivalEstimate\b[\s\S]*?\/>/g) || [];
  assert.equal(
    estimates.length,
    1,
    'the next stop line alone retains the arrival',
  );
  assert.match(page, /class="fleet-truck-panel"\s+hidden=/);
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
