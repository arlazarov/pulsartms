import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

test('next-load wiring retains identity without a plan and rejects stale selection responses', () => {
  const page = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor.cs', import.meta.url),
    'utf8',
  );
  const next = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.NextLoads.cs', import.meta.url),
    'utf8',
  );
  assert.match(page, /_planningDispatchId = result\.Response\?\.DispatchId/);
  assert.match(page, /_planningDispatchId = cached\?\.DispatchId/);
  assert.match(page, /_planningDispatchId = null/);
  assert.match(next, /var currentId = SelectedDispatchId/);
  assert.match(next, /var executionLegId = SelectedExecutionLegId/);
  assert.match(next, /var assignmentRevision = SelectedAssignmentRevision/);
  assert.match(
    next,
    /var identity = \(truckId, currentId, executionLegId, assignmentRevision\)/,
  );
  assert.match(
    next,
    /clearNextLoads"\);\s*if \(!IsCurrentNextLoads\(version, identity\)\)\s*return;/,
  );
  assert.match(
    next,
    /if \(currentId is null\)\s*\{\s*_nextLoadsMessage = null;\s*return;/,
  );
  for (const [captured, selected] of [
    ['Truck', '_activeTruckId'],
    ['Dispatch', 'SelectedDispatchId'],
    ['ExecutionLeg', 'SelectedExecutionLegId'],
    ['AssignmentRevision', 'SelectedAssignmentRevision'],
  ])
    assert.match(next, new RegExp(`identity\\.${captured} == ${selected}`));
  assert.match(next, /version == _nextLoadsVersion/);
  assert.match(
    next,
    /Where\(x =>\s*x\.Id != currentId \|\| x\.ExecutionLegId != executionLegId\s*\)/,
  );
  assert.doesNotMatch(next, /Where\(x => x\.Id != currentId\)/);
  assert.match(
    next,
    /route\.Id == label\.Id && route\.ExecutionLegId == label\.ExecutionLegId/,
  );
  assert.match(
    next,
    /request\.Token\s*\);\s*if \(!IsCurrentNextLoads\(version, identity\)\)\s*return;/,
  );
  assert.match(
    next,
    /"setNextLoadsBytes", payload\);\s*if \(IsCurrentNextLoads\(version, identity\)\)/,
  );
  assert.match(
    next,
    /if \(response.Response.Unchanged\)\s*\{[\s\S]*?return;\s*\}\s*if \(response.Response.Routes is null/,
  );
  assert.ok(
    next.indexOf('response.Response.Unchanged') <
      next.indexOf('new ResponsiveWriteStream'),
  );
  assert.match(next, /_nextLoadsRevision = null/);
  assert.match(
    next,
    /void ResetNextLoads\(\)[\s\S]*?_nextLoadsIdentity = null;[\s\S]*?_nextLoadsRevision = null;/,
  );
  assert.match(
    page,
    /ResetNextLoads\(\);\s*await _map.InvokeVoidAsync\(\s*"setInspectorMode",\s*"truck",\s*truckId\?\.ToString\(\)\s*\);\s*if \(_disposed \|\| selectionVersion != _selectionVersion\)\s*return;\s*await _map.InvokeVoidAsync\("clearNextLoads"\);\s*if \(_disposed \|\| selectionVersion != _selectionVersion\)\s*return;/,
  );
  assert.match(
    page,
    /await Task.WhenAll\(\s*detailsTask,\s*nextLoadsTask,\s*!_disposed && selectionVersion == _selectionVersion\s*\? LoadRouteAsync/,
  );
  assert.ok(
    next.indexOf('_nextLoadsRevision = response.Response.Revision') >
      next.indexOf('"setNextLoadsBytes"'),
  );
});
