const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const escape = (s) =>
  String(s ?? '').replace(
    /[<>&"']/g,
    (c) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&#39;' })[c],
  );
function nodes() {
  const map = new Map();
  return (id) => {
    if (!map.has(id))
      map.set(id, {
        innerHTML: '',
        textContent: '',
        value: 'approval',
        hidden: false,
        dataset: {},
        setAttribute() {},
        scrollIntoView() {},
      });
    return map.get(id);
  };
}
test('seller returns list escapes reasons and gives explicit approval controls only for requested returns', () => {
  const context = {
    G4: {
      $() {},
      api() {},
      esc: escape,
      money: (v) => '$' + v,
      date: (v) => v,
      safe: (f) => f(),
      pager() {},
      returnStatus: (v) => v,
    },
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/seller-returns.js', 'utf8'), context);
  const row = {
    id: 1,
    orderId: 5,
    status: 'Requested',
    buyerName: '<script>',
    productTitle: '<img>',
    description: 'bad <html>',
    pricingPolicyVersion: 1,
    buyerRefundAmount: 95,
    returnShippingFee: 5,
    shippingPayer: 'Buyer',
    reasonCode: 'ChangedMind',
  };
  const html = context.G4.SellerReturns.row(row);
  assert.match(html, /&lt;script&gt;/);
  assert.doesNotMatch(html, /<script>|<img>/);
  assert.match(html, /approve-return/);
  assert.match(html, /Buyer chịu phí/);
  assert.doesNotMatch(
    context.G4.SellerReturns.row({ ...row, status: 'Refunded' }),
    /data-action="approve-return"/,
  );
});
function paypal(api) {
  const $ = nodes(),
    values = new Map(),
    redirects = [];
  const context = {
    G4: { $, api, esc: escape, money: (n) => '$' + n, safe: (f) => f() },
    sessionStorage: { getItem: (k) => values.get(k), setItem: (k, v) => values.set(k, v) },
    location: { assign: (v) => redirects.push(v), search: '' },
    URLSearchParams,
    crypto: { randomUUID: () => 'same-creation-key' },
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/paypal-cart.js', 'utf8'), context);
  return { $, ui: context.G4.PayPalCart, redirects };
}
test('cart PayPal lost creation response preserves batch and idempotency key across retry', async () => {
  const calls = [];
  const h = paypal(async (path, method, body) => {
    calls.push({ path, body });
    throw new Error('Lost reply');
  });
  await assert.rejects(h.ui.begin('original-batch'));
  await assert.rejects(h.ui.begin('original-batch'));
  assert.deepEqual(calls[0], calls[1]);
  assert.equal(calls[0].body.checkoutKey, 'original-batch');
  assert.match(h.$('paypal-cart-result').innerHTML, /chưa rõ/);
  assert.equal(h.redirects.length, 0);
});
test('cart receipt reports per-order status and does not offer repayment during verification', () => {
  const h = paypal();
  const html = h.ui.html({
    id: 1,
    status: 'Verifying',
    message: '<script>',
    orders: [
      { id: 1, amount: 10, paymentStatus: 'Succeeded', orderStatus: 'Paid' },
      { id: 2, amount: 20, paymentStatus: 'Verifying', orderStatus: 'AwaitingPayment' },
    ],
  });
  assert.match(html, /Đã thanh toán/);
  assert.match(html, /Đang xác minh/);
  assert.match(html, /&lt;script&gt;/);
  assert.doesNotMatch(html, /paypal-cart-retry|paypal-cart-approve|paypal-cart-cancel/);
});
test('cart PayPal approval redirects once for all server-confirmed orders', async () => {
  const h = paypal(async () => ({
    id: 1,
    checkoutKey: 'cart',
    status: 'Pending',
    approvalUrl: 'https://www.sandbox.paypal.com/approve',
    orders: [],
  }));
  await h.ui.begin('cart');
  assert.equal(h.redirects.length, 1);
  assert.match(h.$('paypal-cart-result').innerHTML, /cả giỏ/);
});
test('realtime defers refresh while editing and processes queued updates after focus leaves', async () => {
  const timers = new Map(); // No real timers or sockets in this isolated client check.
  const node = nodes();
  let edit = true,
    refreshes = 0;
  const conn = {
    state: 'Connected',
    on() {},
    onreconnecting() {},
    onreconnected() {},
    onclose() {},
    start: async () => {},
  };
  class Builder {
    withUrl() {
      return this;
    }
    configureLogging() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    build() {
      return conn;
    }
  }
  const context = {
    G4: {},
    signalR: { HubConnectionBuilder: Builder, LogLevel: { Error: 4 } },
    document: {
      getElementById: node,
      querySelector: () => null,
      activeElement: { closest: () => (edit ? {} : null) },
      addEventListener() {},
      hidden: false,
    },
    setTimeout: (fn) => {
      timers.set(timers.size + 1, fn);
      return timers.size;
    },
    clearTimeout() {},
    setInterval() {},
    fetch: async () => ({
      ok: true,
      json: async () => ({ url: '/hubs/commerce', token: 'memory-only-test-token' }),
    }),
    addEventListener() {},
  };
  context.window = context;
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/realtime.js', 'utf8'), context);
  context.G4.Realtime.subscribe(['returns'], async () => refreshes++);
  context.G4.Realtime.invalidate(['returns']);
  await context.G4.Realtime.flush();
  assert.equal(refreshes, 0);
  edit = false;
  await context.G4.Realtime.flush();
  assert.equal(refreshes, 1);
});
