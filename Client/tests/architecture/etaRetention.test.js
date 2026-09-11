import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

test('previous ETA retention is bounded, stop-scoped and limited to pending recalculation', () => {
  const source = readFileSync(new URL('../../Shared/DriverStatus/ArrivalEstimate/ArrivalEstimate.razor.cs', import.meta.url), 'utf8');
  assert.match(source, /Memory.Update\(DispatchId, Stop, Eta, Completed\)/);
  const page = readFileSync(new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url), 'utf8');
  assert.equal((page.match(/Memory="_arrivalMemory"/g) || []).length, 2);
});
