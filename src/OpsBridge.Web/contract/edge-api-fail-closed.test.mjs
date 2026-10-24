import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

const source = readFileSync(new URL('../functions/api/v1/[[path]].ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;

function loadHandler(fetchImpl) {
  const exports = {};
  new Function('exports', 'fetch', compiled)(exports, fetchImpl);
  return exports.onRequest;
}

function context(url, env = {}) {
  return { request: new Request(url, { headers: { Authorization: 'Bearer test' } }), env };
}

test('unconfigured edge API returns 503 and never returns demo business records', async () => {
  const handler = loadHandler(() => { throw new Error('must not fetch'); });
  for (const suffix of ['', '?demo=true']) {
    const response = await handler(context(`https://ops.pages.dev/api/v1/purchase-orders${suffix}`));
    assert.equal(response.status, 503);
    assert.equal(response.headers.get('Cache-Control'), 'private, no-store');
    assert.doesNotMatch(await response.text(), /PO-2026-1001/);
  }
});

test('upstream failure never falls back to synthetic data', async () => {
  const handler = loadHandler(() => { throw new Error('broker disconnected'); });
  const response = await handler(context(
    'https://ops.pages.dev/api/v1/purchase-orders?demo=true',
    { API_BACKEND_URL: 'https://api.example.test', DEMO_MODE: 'true' },
  ));
  assert.equal(response.status, 503);
  assert.equal(response.headers.get('Cache-Control'), 'private, no-store');
  assert.doesNotMatch(await response.text(), /api\.example\.test|broker disconnected|PO-2026-1001/);
});

test('live response forwards credentials and disables shared caching', async () => {
  let target;
  let credential;
  const handler = loadHandler((url, init) => {
    target = url;
    credential = init.headers.get('Authorization');
    return new Response('{"orders":[]}', { headers: { 'Content-Type': 'application/json' } });
  });
  const response = await handler(context(
    'https://ops.pages.dev/api/v1/purchase-orders?limit=10',
    { API_BACKEND_URL: 'https://api.example.test' },
  ));
  assert.equal(response.status, 200);
  assert.equal(target, 'https://api.example.test/api/v1/purchase-orders?limit=10');
  assert.equal(credential, 'Bearer test');
  assert.equal(response.headers.get('Cache-Control'), 'private, no-store');
});

test('Platform edge prefix is preserved when the BFF routes through the Worker', async () => {
  let target;
  const handler = loadHandler((url) => {
    target = url;
    return new Response('{}');
  });
  const response = await handler(context(
    'https://ops.pages.dev/api/v1/purchase-orders',
    { API_BACKEND_URL: 'https://portfolio-api-edge.workers.dev/opsbridge' },
  ));
  assert.equal(response.status, 200);
  assert.equal(target, 'https://portfolio-api-edge.workers.dev/opsbridge/api/v1/purchase-orders');
});
