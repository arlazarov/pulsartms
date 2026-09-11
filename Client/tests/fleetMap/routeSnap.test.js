import test from 'node:test';
import assert from 'node:assert/strict';
import { matchRoute, createRouteSnapper } from '../../Scripts/fleetMap/trucks/routeSnap.js';

const path = [{lat:40,lng:-80},{lat:40,lng:-79.99}];
const position = {latitude:40+10/111320,longitude:-79.995,heading:90,speed:60,gpsTime:100};
test('nearby same-direction route corrects only visual coordinates', () => {
  const original = {...position};
  const offset = matchRoute(position,path,[0,1],.5);
  assert.ok(Math.abs(position.latitude + offset.latitude - 40) < 1e-9);
  assert.deepEqual(position,original);
});
test('does not snap opposing, distant, stopped or out-of-progress positions', () => {
  for (const p of [{...position,heading:270},{...position,speed:0},
    {...position,latitude:40+60/111320}]) assert.equal(matchRoute(p,path,[0,1],.5),null);
  assert.equal(matchRoute(position,path,[0,1],30),null);
  assert.equal(matchRoute(position,path,[0,1],null),null);
});
test('correction fades out at distance boundary', () => {
  const p = {...position,latitude:40+39/111320};
  const result = matchRoute(p,path,[0,1],.5);
  assert.ok(Math.abs(result.latitude)*111320 < 3);
});
test('smooth correction, gradual release and isolated route state', () => {
  const snap = createRouteSnapper();
  let calls=0, enabled=true;
  const match=()=>{calls++;return enabled?{latitude:-.0001,longitude:0}:null;};
  let result;
  for(let now=0;now<=1000;now+=10) result=snap(position,'a',match,now);
  assert.equal(calls,11);
  assert.ok(result.latitude < position.latitude);
  enabled=false;
  const before=result.latitude;
  result=snap(position,'a',match,1100);
  assert.ok(result.latitude>before && result.latitude<position.latitude);
  assert.deepEqual(snap(position,'b',match,1110),position);
});
