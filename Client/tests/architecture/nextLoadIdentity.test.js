import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

test('next-load wiring retains identity without a plan and rejects stale selection responses', () => {
  const page = readFileSync(new URL('../../Pages/FleetMap/FleetMap.razor.cs', import.meta.url), 'utf8');
  const next = readFileSync(new URL('../../Pages/FleetMap/FleetMap.NextLoads.cs', import.meta.url), 'utf8');
  assert.match(page, /_planningDispatchId = result\.Response\?\.DispatchId/);
  assert.match(page, /_planningDispatchId = cached\?\.DispatchId/);
  assert.match(page, /_planningDispatchId = null/);
  assert.match(next, /var currentId = SelectedDispatchId/);
  assert.match(next, /clearNextLoads"\);\s*if \(!IsCurrentNextLoads\(version, truckId, currentId\)\) return;/);
  assert.match(next, /if \(currentId is null\)\s*\{\s*_nextLoadsMessage = null;\s*return;/);
  assert.match(next, /currentId != SelectedDispatchId/);
  assert.match(next, /Where\(x => x\.Id != currentId\)/);
  assert.match(next, /currentId == SelectedDispatchId/);
  assert.match(next, /if \(response.Response.Unchanged\)\s*\{[\s\S]*?return;\s*\}\s*if \(response.Response.Routes is null/);
  assert.ok(next.indexOf('response.Response.Unchanged') < next.indexOf('new ResponsiveWriteStream'));
  assert.match(next, /_nextLoadsRevision = null/);
  assert.match(next, /void ResetNextLoads\(\)[\s\S]*?_nextLoadsIdentity = null;[\s\S]*?_nextLoadsRevision = null;/);
  assert.match(page, /ResetNextLoads\(\);\s*await _map.InvokeVoidAsync\("setInspectorMode", "truck", truckId\?\.ToString\(\)\);\s*if \(_disposed \|\| selectionVersion != _selectionVersion\) return;\s*await _map.InvokeVoidAsync\("clearNextLoads"\);\s*if \(_disposed \|\| selectionVersion != _selectionVersion\) return;/);
  assert.match(page, /await Task.WhenAll\(detailsTask, nextLoadsTask,\s*!_disposed && selectionVersion == _selectionVersion \? LoadRouteAsync/);
  assert.ok(next.indexOf('_nextLoadsRevision = response.Response.Revision') > next.indexOf('"setNextLoadsBytes"'));
});
