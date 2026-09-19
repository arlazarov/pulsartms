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
  const estimates = page.match(/<ArrivalEstimate\b[^>]*\/>/g) || [];
  assert.equal(
    estimates.length,
    1,
    'loading and ready use one retained forecast component',
  );
  for (const estimate of estimates) {
    assert.match(estimate, /Memory="_arrivalMemory"/);
    assert.match(estimate, /DispatchId="SelectedDispatchId"/);
    assert.match(estimate, /Stop="nextStop"/);
    assert.match(estimate, /Refreshing="_etaRefreshPending"/);
  }
});
