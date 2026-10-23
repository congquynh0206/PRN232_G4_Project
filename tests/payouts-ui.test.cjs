const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
function load(api = async () => ({ status: 'Created' })) {
  let key = 0;
  const context = {
    G4: {
      api,
      money: (x) => '$' + Number(x).toFixed(2),
      date: (x) => x,
      esc: (x) =>
        String(x ?? '').replace(
          /[&<>"']/g,
          (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
        ),
    },
    crypto: { randomUUID: () => 'key-' + ++key },
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/payouts-ui.js', 'utf8'), context);
  return context.G4.Payouts;
}
test('payout progress distinguishes pending bank acknowledgement, returns and historical records', () => {
  const ui = load(),
    payout = {
      id: 7,
      amount: 50,
      status: 'FundsSent',
      destinationMasked: '<img>',
      createdAt: '2026-10-20T10:00:00',
      workflowVersion: 1,
      history: [
        {
          toStatus: 'Created',
          actorRole: 'seller',
          actorUserId: 3,
          occurredAt: '2026-10-20T10:00:00',
        },
        {
          toStatus: 'FundsSent',
          actorRole: 'system',
          occurredAt: '2026-10-20T10:00:05Z',
          bankReferenceId: '<script>',
        },
      ],
    };
  const html = ui.card(payout);
  assert.match(html, /chờ xác nhận/);
  assert.match(html, /Seller #3/);
  assert.match(html, /Hệ thống/);
  assert.match(html, /10:00:00Z/);
  assert.match(html, /&lt;img&gt;|&lt;script&gt;/);
  assert.doesNotMatch(html, /<img>|<script>/);
  assert.match(ui.card({ ...payout, status: 'Returned' }), /bù nợ nếu có/);
  assert.match(
    ui.card({ ...payout, status: 'Completed', workflowVersion: 0, history: [] }),
    /Dữ liệu cũ chưa ghi nhận/,
  );
});
test('unknown payout response retries exactly the same key; changed amount cannot create another withdrawal', async () => {
  const calls = [];
  let fail = true;
  const ui = load(async (route, method, payload) => {
    calls.push({ ...payload });
    if (fail) {
      fail = false;
      throw new Error('network');
    }
    return { status: 'Created' };
  });
  await assert.rejects(ui.request(50, false), /network/);
  await assert.rejects(ui.request(60, false), /Kết quả yêu cầu trước chưa rõ/);
  await ui.request(50, false);
  assert.equal(calls.length, 2);
  assert.deepEqual(calls[0], calls[1]);
  await ui.request(5, false);
  assert.notEqual(calls[2].key, calls[0].key);
});
test('parallel submit posts once; definitive rejection allows a corrected new request', async () => {
  let resolve,
    calls = 0;
  const ui = load(() => {
    calls++;
    return new Promise((r) => (resolve = r));
  });
  const first = ui.request(50, false);
  assert.equal(await ui.request(50, false), null);
  assert.equal(calls, 1);
  resolve({ status: 'Created' });
  await first;
  const keys = [];
  let reject = true;
  const corrected = load(async (url, method, p) => {
    keys.push(p.key);
    if (reject) {
      reject = false;
      throw Object.assign(new Error('insufficient'), { status: 409 });
    }
    return { status: 'Created' };
  });
  await assert.rejects(corrected.request(50, false), /insufficient/);
  await corrected.request(10, false);
  assert.notEqual(keys[0], keys[1]);
});
