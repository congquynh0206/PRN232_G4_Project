const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
function registration(fetch) {
  const nodes = new Map();
  const node = (id) => {
    if (!nodes.has(id))
      nodes.set(id, {
        value: '',
        textContent: '',
        hidden: id === 'registration-success',
        disabled: false,
        action: '/Account/RegisterSeller',
        setAttribute(k, v) {
          this[k] = v;
        },
        focus() {
          this.focused = true;
        },
      });
    return nodes.get(id);
  };
  const buttons = ['valid', 'invalid', 'blacklisted'].map((name) => ({
    dataset: { registrationSample: name },
    disabled: false,
  }));
  node('registration-kind').value = 'CCCD';
  const context = {
    document: {
      getElementById: node,
      querySelectorAll: () => buttons,
      querySelector: () => ({ value: 'csrf' }),
    },
    crypto: { randomUUID: () => 'same-registration-key' },
    fetch,
  };
  context.window = context;
  vm.createContext(context);
  vm.runInContext(fs.readFileSync('frontend/wwwroot/js/seller-registration.js', 'utf8'), context);
  function fill() {
    node('registration-username').value = 'Seller';
    node('registration-shop').value = 'Shop';
    node('registration-email').value = 'seller@example.test';
    node('registration-password').value = 'DemoSeller123!';
    node('registration-confirm').value = 'DemoSeller123!';
    node('registration-identity').value = '999111111111';
  }
  fill();
  return { ui: context.G4Registration, node, buttons };
}
test('registration retry freezes reviewed fields and reuses identical payload after unknown network result', async () => {
  const requests = [];
  const { ui, node } = registration(async (path, request) => {
    requests.push(JSON.parse(request.body));
    throw new Error('Connection lost');
  });
  await ui.submit({ preventDefault() {} });
  assert.equal(node('registration-fields').disabled, true);
  assert.equal(node('registration-submit').disabled, false);
  assert.equal(node('registration-error').focused, true);
  node('registration-email').value = 'changed@example.test';
  await ui.submit({ preventDefault() {} });
  assert.deepEqual(requests[0], requests[1]);
  assert.equal(requests[1].email, 'seller@example.test');
  assert.match(node('registration-submit').textContent, /cùng yêu cầu/);
});
test('blacklist 403 does not claim account creation; fields unlock for a corrected identity', async () => {
  const { ui, node } = registration(async () => ({
    ok: false,
    status: 403,
    json: async () => ({ error: 'Không thể đăng ký hoặc cập nhật thông tin người bán này.' }),
  }));
  await ui.submit({ preventDefault() {} });
  assert.equal(node('registration-success').hidden, true);
  assert.equal(node('registration-success').textContent, '');
  assert.equal(node('registration-fields').disabled, false);
  assert.match(node('registration-error').textContent, /Không thể đăng ký/);
});
test('successful registration shows active/zero balance, clears password fields and prevents duplicate submit', async () => {
  let count = 0;
  const { ui, node } = registration(async () => {
    count++;
    return { ok: true, json: async () => ({ email: 'seller@example.test', storeName: '<img>' }) };
  });
  await ui.submit({ preventDefault() {} });
  await ui.submit({ preventDefault() {} });
  assert.equal(count, 1);
  assert.equal(node('registration-password').value, '');
  assert.equal(node('registration-confirm').value, '');
  assert.equal(node('registration-submit').disabled, true);
  assert.match(node('registration-success').textContent, /số dư 0/);
});
test('password mismatch stops before any request; mock samples populate only documented synthetic fields', async () => {
  let count = 0;
  const { ui, node } = registration(async () => {
    count++;
    return {
      ok: true,
      json: async () => [
        { name: 'blacklisted', identity: { kind: 'CCCD', value: '999000000002' } },
      ],
    };
  });
  node('registration-confirm').value = 'wrong';
  await ui.submit({ preventDefault() {} });
  assert.equal(count, 0);
  await ui.sample('blacklisted');
  assert.equal(node('registration-identity').value, '999000000002');
  assert.equal(node('registration-password').value, node('registration-confirm').value);
  assert.match(node('registration-password-help').textContent, /Mật khẩu mẫu: DemoSeller123!/);
  assert.equal(node('registration-fields').disabled, false);
});
test('write-off report escapes all untrusted values and distinguishes fixture blacklist from real decisions', () => {
  const context = {
    G4: {
      $() {},
      api() {},
      safe() {},
      pager() {},
      date: (s) => s,
      money: (v) => '$' + Number(v).toFixed(2),
      esc: (s) =>
        String(s ?? '').replace(
          /[<>&"']/g,
          (c) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&#39;' })[c],
        ),
    },
    URLSearchParams,
  };
  vm.createContext(context);
  vm.runInContext(fs.readFileSync('frontend/wwwroot/js/seller-risk.js', 'utf8'), context);
  const row = {
    id: 1,
    sellerId: 3,
    originalAmount: 40,
    recoveredAmount: 10,
    remainingWrittenOffAmount: 30,
    currentDebt: 35,
    blacklistedIdentities: 0,
    reason: '<script>',
  };
  const html = context.G4.SellerRisk.decisionsHtml([row]);
  assert.match(html, /&lt;script&gt;/);
  assert.doesNotMatch(html, /<script>/);
  assert.match(html, /Nợ lúc write-off: \$40.00/);
  assert.match(html, /Đã thu hồi sau quyết định: \$10.00/);
  assert.match(html, /Nợ hiện tại của seller: \$35.00/);
  assert.match(html, /Chưa có định danh/);
  assert.match(
    context.G4.SellerRisk.blacklistHtml([{ isDemo: true, maskedValue: '<img>' }]),
    /Dữ liệu mẫu Development/,
  );
});
