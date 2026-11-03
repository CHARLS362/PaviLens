import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const decoder = fileURLToPath(new URL('./decode.mjs', import.meta.url));
const response = text => ({ status: 'completed', output: [{ type: 'message', content: [{ type: 'output_text', text }] }] });
function run(body) { return spawnSync(process.execPath, [decoder], { input: JSON.stringify(body), encoding: 'utf8' }); }

test('decodes the documented Meta mask into source-pixel contours with confidence', () => {
  // Wire fixture from https://dev.meta.ai/docs/sam/reading-segmentation, confidence enabled.
  const text = "<0f>0<|box;x1=211;y1=228;x2=270;y2=254;w=320;h=334;c=0.9|><|mask;x=0;y=0;c=0.9;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7`e.ioeAf6-k/#Xd8%dX9x(|>";
  const result = run(response(text));
  assert.equal(result.status, 0, result.stderr);
  const object = JSON.parse(result.stdout).objects[0];
  assert.equal(object.confidence, .9);
  assert.ok(object.rings.length > 0);
  for (const ring of object.rings) for (const [x,y] of ring) {
    assert.ok(x >= 211 && x <= 271 && y >= 228 && y <= 255);
  }
});
test('accepts a completed empty match set', () => {
  const result = run(response(''));
  assert.equal(result.status, 0); assert.deepEqual(JSON.parse(result.stdout), { objects: [] });
});
test('rejects incomplete and corrupt masks instead of measuring partial data', () => {
  assert.notEqual(run({ status: 'incomplete', output: [] }).status, 0);
  assert.notEqual(run(response('<0f>0<|box;broken|><|mask;broken|>')).status, 0);
});
