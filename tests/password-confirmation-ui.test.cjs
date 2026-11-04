const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function common(fetch) {
  const elements = new Map();
  const context = { window: {}, URL, fetch, setTimeout, clearTimeout,
    document: { getElementById(id) {
      if (!elements.has(id)) elements.set(id, {});
      return elements.get(id);
    } } };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/common.js', 'utf8'), context);
  return context.window.G4;
}
const reply = (status, data) => ({ status, ok: status === 200,
  headers: { get: () => null }, json: async () => data });

test('password challenge freezes reviewed payload/key and encodes Unicode for one explicit retry', async () => {
  const sent = [];
  const payload = { amount: 10, key: 'reviewed-key' };
  const ui = common(async (_, request) => {
    sent.push(request);
    return sent.length === 1
      ? reply(428, { code: 'PasswordConfirmationRequired', action: 'Rút tiền' })
      : reply(200, { id: 1 });
  });
  ui.PasswordConfirmation = { run: async (action, path, serialized, send) => {
    assert.equal(action, 'Rút tiền');
    assert.equal(path, 'seller/payouts');
    payload.amount = 999;
    payload.key = 'changed';
    return send('Mật khẩu 123!');
  } };
  assert.equal((await ui.api('seller/payouts', 'POST', payload)).id, 1);
  assert.equal(sent.length, 2);
  assert.equal(sent[0].body, sent[1].body);
  assert.deepEqual(JSON.parse(sent[1].body), { amount: 10, key: 'reviewed-key' });
  assert.equal(decodeURIComponent(sent[1].headers['X-Action-Password']), 'Mật khẩu 123!');
  assert.equal(sent[0].headers['X-Action-Password'], undefined);
});

test('network errors and normal permission errors never trigger confirmation or auto retry', async () => {
  for (const failure of [() => { throw new Error('network'); },
    () => reply(403, { error: 'Không có quyền.' })]) {
    let calls = 0;
    const ui = common(async () => { calls++; return failure(); });
    ui.PasswordConfirmation = { run: () => assert.fail('unexpected dialog') };
    await assert.rejects(ui.api('seller/payouts', 'POST', { amount: 10, key: 'same-key' }));
    assert.equal(calls, 1);
  }
});

test('each operation needs a fresh password; invalid-password code reaches the dialog', async () => {
  let dialogs = 0;
  const ui = common(async (_, request) => request.headers['X-Action-Password']
    ? reply(403, { code: 'PasswordConfirmationInvalid', error: 'Mật khẩu không đúng.' })
    : reply(428, { code: 'PasswordConfirmationRequired', action: 'Rút tiền' }));
  ui.PasswordConfirmation = { run: async (_, __, ___, send) => {
    dialogs++;
    await assert.rejects(send('wrong'), (error) => error.code === 'PasswordConfirmationInvalid');
    return {};
  } };
  await ui.api('seller/payouts', 'POST', {});
  await ui.api('seller/payouts', 'POST', {});
  assert.equal(dialogs, 2);
});
