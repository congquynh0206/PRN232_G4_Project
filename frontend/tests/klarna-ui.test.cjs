const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const frontend = path.resolve(__dirname, '..');
const source = (name) => fs.readFileSync(path.join(frontend, name), 'utf8');

function harness(reply) {
  const calls = [], redirects = [], saved = new Map();
  let nextKey = 0;
  const context = {
    G4: {
      api: async (route, method, body) => {
        calls.push({ route, method, body });
        return reply(route);
      },
    },
    crypto: { randomUUID: () => ++nextKey === 1 ? 'klarna-attempt-key' : 'new-attempt-key' },
    sessionStorage: {
      getItem: (key) => saved.get(key) ?? null,
      setItem: (key, value) => saved.set(key, value),
      removeItem: (key) => saved.delete(key),
    },
    URL,
    location: { assign: (url) => redirects.push(url) },
  };
  vm.runInNewContext(source('wwwroot/js/klarna.js'), context);
  return { ...context, calls, redirects, saved };
}

test('Klarna starts an order with a key and redirects to the HPP', async () => {
  const h = harness(() => ({ status: 'Pending', redirectUrl: 'https://pay.example.test/hpp' }));
  await h.G4.Klarna.begin(42);
  assert.equal(h.calls.length, 1);
  assert.equal(h.calls[0].route, 'orders/42/pay/klarna');
  assert.equal(h.calls[0].method, 'POST');
  assert.equal(h.calls[0].body.key, 'klarna-attempt-key');
  assert.deepEqual(h.redirects, ['https://pay.example.test/hpp']);
});

test('missing or unsafe HPP URL does not navigate', async () => {
  for (const redirectUrl of [null, 'javascript:alert(1)', 'http://pay.example.test/hpp']) {
    const h = harness(() => ({ status: 'Pending', redirectUrl }));
    await assert.rejects(h.G4.Klarna.begin(42), /Klarna/);
    assert.deepEqual(h.redirects, []);
  }
});

test('reconciliation is POST and never redirects or claims success', async () => {
  const h = harness(() => ({ id: 8, status: 'Verifying', errorCode: null }));
  const result = await h.G4.Klarna.reconcile(42);
  assert.equal(result.status, 'Verifying');
  assert.equal(h.calls[0].route, 'orders/42/pay/klarna/reconcile');
  assert.equal(h.calls[0].method, 'POST');
  assert.deepEqual(h.redirects, []);
});

test('buyer checkout and return routes wire the order-specific flow', () => {
  const view = source('Views/Buyer/Index.cshtml');
  const cart = source('wwwroot/js/buyer-cart.js');
  const orders = source('wwwroot/js/buyer.js');
  const controller = source('Controllers/BuyerController.cs');
  assert.match(view, /name="method" value="klarna"/);
  assert.match(view, /src="~\/js\/klarna.js"/);
  assert.match(cart, /G4\.Klarna\.begin\(pending\[0\]\.id\)/);
  assert.match(orders, /data-action="reconcile-klarna"/);
  assert.match(orders, /!o\.payPalCheckoutId && !klarnaInFlight/);
  assert.match(orders, /G4\.Klarna\.reconcile\(id\)\.then/);
  assert.match(orders, /}, 30000\)/);
  assert.match(controller, /KlarnaReturn\(int orderId, CancellationToken ct\)/);
  assert.match(controller, /KlarnaCancel\(int orderId\)/);
  assert.match(controller, /api\/orders\/\{orderId\}\/pay\/klarna\/reconcile/);
  assert.match(controller, /User\.FindFirstValue\("access_token"\)/);
  assert.match(controller, /KlarnaCancel[\s\S]*?paymentResult = "cancelled"/);
});

test('uncertain start reuses key; terminal reconciliation permits a fresh attempt', async () => {
  let result = { status: 'Pending', redirectUrl: 'https://pay.example.test/hpp' };
  const h = harness(() => result);
  await h.G4.Klarna.begin(42);
  await h.G4.Klarna.begin(42);
  assert.equal(h.calls[0].body.key, h.calls[1].body.key);
  assert.equal(h.saved.get('g4-klarna-attempt:42'), 'klarna-attempt-key');
  result = { id: 42, status: 'Failed', errorCode: 'DECLINED' };
  await h.G4.Klarna.reconcile(42);
  assert.equal(h.saved.has('g4-klarna-attempt:42'), false);
  result = { status: 'Pending', redirectUrl: 'https://pay.example.test/hpp' };
  await h.G4.Klarna.begin(42, 77);
  assert.equal(h.calls.at(-1).body.key, 'new-attempt-key');
  await h.G4.Klarna.begin(42, 77);
  assert.equal(h.calls.at(-1).body.key, 'new-attempt-key');
});

test('authorization is approval, not capture, and clears the attempt key', async () => {
  let result = { status: 'Pending', redirectUrl: 'https://pay.example.test/hpp' };
  const h = harness(() => result);
  await h.G4.Klarna.begin(8);
  result = { id: 8, status: 'Authorized', errorCode: null };
  assert.equal((await h.G4.Klarna.reconcile(8)).status, 'Authorized');
  assert.equal(h.saved.has('g4-klarna-attempt:8'), false);
  const orders = source('wwwroot/js/buyer.js');
  assert.match(orders, /klarnaAuthorized.*'Authorized'/);
  assert.match(orders, /Chưa thu tiền; tiền sẽ được thu khi giao hàng/);
  assert.match(orders, /result.status === 'Authorized'/);
  assert.match(orders, /payment.status === 'Succeeded'/);
  assert.match(source('wwwroot/js/common.js'), /source === 'buyer' && o.status === 'Paid' \? 'Sẵn sàng giao hàng'/);
  assert.ok(orders.includes("p.get('paymentProvider') !== 'klarna'"));
});
