import test from 'node:test';
import assert from 'node:assert/strict';
import {secretFindings} from '../../../scripts/git-safety.mjs';

test('history guard rejects private paths and secret configuration without echoing values', () => {
  assert.ok(secretFindings('.env.production', 'anything').length);
  assert.ok(secretFindings('Server/API/gmail-token/session.json', '{}').length);
  assert.ok(secretFindings('artifacts/output.txt', 'anything').length);
  const result = secretFindings('Server/API/appsettings.json', JSON.stringify({Provider: {ApiKey: 'fixture-value'}}));
  assert.ok(result.length);
  assert.ok(result.every(message => !message.includes('fixture-value')));
  assert.deepEqual(secretFindings('Client/wwwroot/appsettings.example.json', '{"GoogleMaps":{"ApiKey":""}}'), []);
});

test('history guard recognizes private key and provider token signatures', () => {
  assert.ok(secretFindings('source.txt', ['-----BEGIN ', 'PRIVATE KEY-----'].join('')).length);
  assert.ok(secretFindings('source.txt', 'AKIA' + 'A'.repeat(16)).length);
  assert.ok(secretFindings('source.txt', 'AIza' + 'A'.repeat(35)).length);
});
