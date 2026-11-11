// Buy It Now inside the "Thanh toán" tab: buyer-cart.js owns the tab, buyer-buy-now.js the
// one-product flow (/Buyer?tab=checkout&product={id}#checkout).
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const tick = () => new Promise((resolve) => setImmediate(resolve));

async function harness(handler = async (route) => {
  throw new Error(route);
}, { search = '?product=1', product: overrides = {} } = {}) {
  const elements = new Map();
  const messages = [];
  const redirects = [];
  const calls = [];
  const tabs = [];
  const shown = [];
  const method = { value: 'card' };
  const element = (id) => {
    if (!elements.has(id))
      elements.set(id, {
        value: id === 'card-number' ? '4111111111111111' : id === 'card-expiry' ? '12/30' : '',
        disabled: false,
        hidden: false,
        checked: false,
        innerHTML: '',
        textContent: '',
        dataset: {},
        classList: { toggle() {}, add() {}, remove() {}, contains: () => false },
        setAttribute() {},
        addEventListener() {},
        querySelectorAll: () => [],
        closest() {
          this.label ??= { hidden: false };
          return this.label;
        },
        focus() {},
      });
    return elements.get(id);
  };
  const product = {
    id: 1,
    title: '<img onerror=attack()>',
    price: 10,
    weightKg: 1,
    available: 3,
    sellerId: 7,
    sellerName: '<Shop>',
    immediatePaymentRequired: false,
    ...overrides,
  };
  const quote = {
    subtotal: 10,
    discount: 0,
    shipping: 2.1,
    total: 12.1,
    totalWeightKg: 1,
    pricingFingerprint: 'fp',
    lines: [],
    promotions: [],
  };
  const location = {
    search,
    href: 'http://localhost/Buyer' + search + '#checkout',
    assign: (url) => redirects.push(url),
  };
  const G4 = {
    $: element,
    money: (x) => '$' + Number(x || 0).toFixed(2),
    esc: (x) => String(x ?? '').replace(/</g, '&lt;').replace(/>/g, '&gt;'),
    api: async (route, verb, body) => {
      calls.push({ route, verb, body });
      if (route === 'addresses')
        return [
          {
            id: 1,
            fullName: 'Buyer',
            street: '1 Road',
            city: 'Hanoi',
            state: 'Hanoi',
            country: 'Vietnam',
            isDefault: true,
          },
        ];
      if (route === 'catalog/products/1') return product;
      if (route === 'cart') return { version: 'v1', items: [] };
      if (route === 'quote') return quote;
      return handler(route, verb, body);
    },
    safe: (fn) => fn(),
    message: (value, success) => messages.push({ value, success }),
    productThumbnail: () => '',
    Promotions: { linePriceHtml: () => '', breakdownHtml: () => '' },
    Klarna: { begin: async (id) => redirects.push('klarna:' + id) },
  };
  const document = {
    querySelectorAll: (selector) =>
      selector.includes('[data-buy-now-input]')
        ? [element('buy-now-quantity'), element('buy-now-coupon')]
        : [],
    querySelector: (selector) =>
      selector.startsWith('input[name="method"]') ? method : element('query:' + selector),
  };
  const context = {
    G4,
    crypto: { randomUUID: () => Math.random().toString() },
    location,
    history: { replaceState: (state, title, url) => (location.href = String(url)) },
    URL,
    URLSearchParams,
    document,
  };
  for (const file of ['buyer-addresses.js', 'buyer-cart.js', 'buyer-buy-now.js'])
    vm.runInNewContext(fs.readFileSync(`frontend/wwwroot/js/${file}`, 'utf8'), context);
  await G4.BuyerCart.init({
    tab: (id) => tabs.push(id),
    loadOrders: async () => {},
    showOrder: async (id) => shown.push(id),
  });
  const pay = () => element('pay').onclick();
  const paths = (suffix) => calls.filter((x) => x.route.endsWith(suffix));
  return { element, messages, redirects, calls, tabs, shown, method, location, pay, paths };
}

test('Buy It Now quotes one product without touching the cart', async () => {
  const h = await harness();
  const quote = h.calls.find((x) => x.route === 'quote');
  assert.equal(quote.body.addressId, 1);
  assert.equal(JSON.stringify(quote.body.items), JSON.stringify([{ productId: 1, quantity: 1 }]));
  assert.equal(h.calls.some((x) => x.route.startsWith('cart/')), false);
  const html = h.element('checkout-items').innerHTML;
  assert.ok(html.includes('&lt;img'));
  assert.ok(html.includes('&lt;Shop&gt;'));
  assert.ok(!html.includes('<img'));
  assert.equal(h.element('total').textContent, '$12.10');
  assert.equal(h.element('pay').disabled, false);
  assert.equal(h.element('cart-method-paypal').closest().hidden, false);
});

test('normal product: card creates the order once with the viewed total and pays', async () => {
  const h = await harness(async (route) => {
    if (route === 'orders') return { id: 55, status: 'AwaitingPayment' };
    if (route === 'orders/55/pay/card') return { status: 'Succeeded' };
    throw new Error(route);
  });
  await h.pay();
  const order = h.calls.find((x) => x.route === 'orders');
  assert.equal(order.body.expectedTotal, 12.1);
  assert.equal(order.body.pricingFingerprint, 'fp');
  assert.equal(h.paths('/pay/card').length, 1);
  assert.equal(h.paths('/pay/card')[0].body.number, '4111111111111111');
  assert.ok(h.tabs.includes('orders'));
  assert.deepEqual(h.shown, [55]);
  assert.ok(h.messages.at(-1).value.includes('#55') && h.messages.at(-1).success);
  // Buy It Now is over: the tab is back to cart checkout.
  assert.ok(!h.location.href.includes('product='));
});

test('normal product: a declined card leaves the order for payment in Đơn hàng', async () => {
  const h = await harness(async (route) => {
    if (route === 'orders') return { id: 56, status: 'AwaitingPayment' };
    if (route === 'orders/56/pay/card') return { status: 'Failed', errorCode: 'Declined' };
    throw new Error(route);
  });
  await h.pay();
  assert.equal(h.paths('orders').length, 1);
  assert.deepEqual(h.shown, [56]);
  assert.ok(h.messages.at(-1).value.includes('#56'));
  assert.ok(!h.messages.at(-1).success);
});

test('normal product: PayPal creates the order then redirects to PayPal approval', async () => {
  const h = await harness(async (route) => {
    if (route === 'orders') return { id: 57, status: 'AwaitingPayment' };
    if (route === 'orders/57/pay/paypal') return { approvalUrl: 'https://paypal.test/approve' };
    throw new Error(route);
  });
  h.method.value = 'paypal';
  await h.pay();
  assert.equal(h.paths('/pay/card').length, 0);
  assert.deepEqual(h.redirects, ['https://paypal.test/approve']);
});

test('normal product: a rejected order request drops the quote and keeps no order', async () => {
  const h = await harness(async (route) => {
    if (route === 'orders')
      throw Object.assign(new Error('Báo giá đã thay đổi.'), { status: 409 });
    throw new Error(route);
  });
  await assert.rejects(h.pay(), /cập nhật báo giá/);
  assert.equal(h.paths('/pay/card').length, 0);
  assert.equal(h.element('pay').disabled, true);
});

test('immediate product: credit card only, order exists only after capture', async () => {
  const h = await harness(async (route) => {
    if (route === 'buyer/immediate-checkouts')
      return { id: 's1', expiresAt: '2999-01-01T00:00:00Z', quote: { total: 12.1 } };
    if (route === 'buyer/immediate-checkouts/s1/pay/card')
      return { status: 'Succeeded', orderId: 42 };
    throw new Error(route);
  }, { product: { immediatePaymentRequired: true } });
  assert.equal(h.element('cart-method-paypal').closest().hidden, true);
  assert.equal(h.element('cart-method-klarna').closest().hidden, true);
  assert.equal(h.element('immediate-method-note').hidden, false);
  assert.equal(h.element('cart-method-card').checked, true);
  assert.ok(h.element('checkout-items').innerHTML.includes('Yêu cầu thanh toán ngay'));
  await h.pay();
  assert.equal(h.paths('orders').length, 0);
  assert.equal(h.paths('/pay/card').length, 1);
  assert.deepEqual(h.shown, [42]);
});

test('immediate product: double click submits one payment', async () => {
  let resolve;
  const h = await harness(async (route) => {
    if (route === 'buyer/immediate-checkouts')
      return { id: 's1', expiresAt: '2999-01-01T00:00:00Z', quote: { total: 12.1 } };
    return new Promise((r) => (resolve = r));
  }, { product: { immediatePaymentRequired: true } });
  const first = h.pay();
  await h.pay();
  await tick();
  assert.equal(h.paths('/pay/card').length, 1);
  resolve({ status: 'Succeeded', orderId: 42 });
  await first;
  assert.deepEqual(h.shown, [42]);
});

test('immediate product: HTTP uncertainty blocks payment until the server status is read', async () => {
  const h = await harness(async (route) => {
    if (route === 'buyer/immediate-checkouts')
      return { id: 's1', expiresAt: '2999-01-01T00:00:00Z', quote: { total: 12.1 } };
    if (route.endsWith('/pay/card')) throw new Error('Network lost');
    if (route === 'buyer/immediate-checkouts/s1') return { status: 'Succeeded', orderId: 77 };
    throw new Error(route);
  }, { product: { immediatePaymentRequired: true } });
  await assert.rejects(h.pay(), /Kiểm tra kết quả/);
  assert.equal(h.element('pay').disabled, true);
  assert.equal(h.element('buy-now-check').hidden, false);
  assert.ok(h.location.href.includes('checkout=s1'));
  await h.pay();
  assert.equal(h.paths('/pay/card').length, 1);
  await h.element('buy-now-check').onclick();
  assert.deepEqual(h.shown, [77]);
});

test('refresh of a paid session reads the status without charging again', async () => {
  const h = await harness(async (route) => {
    if (route === 'buyer/immediate-checkouts/s9') return { status: 'Succeeded', orderId: 9 };
    throw new Error(route);
  }, { search: '?product=1&checkout=s9', product: { immediatePaymentRequired: true } });
  assert.equal(h.calls.some((x) => x.verb === 'POST' && x.route !== 'quote'), false);
  assert.deepEqual(h.shown, [9]);
});

test('declined attempt retries the same session with a new key; out of stock is explicit', async () => {
  let count = 0;
  const h = await harness(async (route) => {
    if (route === 'buyer/immediate-checkouts')
      return { id: 's1', expiresAt: '2999-01-01T00:00:00Z', quote: { total: 12.1 } };
    if (++count === 1) return { status: 'Failed', errorCode: 'Declined' };
    throw Object.assign(new Error('Out of Stock'), { code: 'OutOfStock' });
  }, { product: { immediatePaymentRequired: true } });
  await h.pay();
  assert.ok(h.messages.at(-1).value.includes('Declined'));
  await h.pay();
  assert.equal(h.paths('buyer/immediate-checkouts').length, 1);
  const keys = h.paths('/pay/card').map((x) => x.body.key);
  assert.notEqual(keys[0], keys[1]);
  assert.ok(h.messages.at(-1).value.includes('Out of Stock'));
  assert.ok(!h.location.href.includes('checkout='));
});

test('changing the quantity re-quotes and starts a new session before paying', async () => {
  const h = await harness(async (route, verb, body) => {
    if (route === 'buyer/immediate-checkouts')
      return {
        id: 's' + body.quantity,
        expiresAt: '2999-01-01T00:00:00Z',
        quote: { total: 12.1 },
      };
    return { status: 'Failed', errorCode: 'Declined' };
  }, { product: { immediatePaymentRequired: true } });
  await h.pay();
  h.element('buy-now-quantity').value = '2';
  h.element('buy-now-quantity').onchange();
  await tick();
  assert.equal(h.calls.filter((x) => x.route === 'quote').at(-1).body.items[0].quantity, 2);
  await h.pay();
  const sessions = h.paths('buyer/immediate-checkouts').map((x) => x.body.quantity);
  assert.deepEqual(sessions, [1, 2]);
});

test('a total that changed when the session starts must be confirmed again', async () => {
  const h = await harness(async (route) => {
    if (route === 'buyer/immediate-checkouts')
      return {
        id: 's1',
        expiresAt: '2999-01-01T00:00:00Z',
        quote: { subtotal: 10, discount: 0, shipping: 3, total: 13, totalWeightKg: 1 },
      };
    throw new Error(route);
  }, { product: { immediatePaymentRequired: true } });
  await h.pay();
  assert.equal(h.paths('/pay/card').length, 0);
  assert.equal(h.element('total').textContent, '$13.00');
  assert.ok(h.messages.at(-1).value.includes('Tổng tiền vừa thay đổi'));
});

test('verifying capture blocks a second payment and reconciles before success', async () => {
  let reconciled = false;
  const h = await harness(async (route) => {
    if (route === 'buyer/immediate-checkouts')
      return { id: 's1', expiresAt: '2999-01-01T00:00:00Z', quote: { total: 12.1 } };
    if (route.endsWith('/pay/card')) return { status: 'Verifying', stage: 'Capturing' };
    if (route.endsWith('/reconcile')) {
      reconciled = true;
      return {};
    }
    return reconciled
      ? { status: 'Succeeded', orderId: 31 }
      : { status: 'Verifying', attempt: { status: 'Verifying' } };
  }, { product: { immediatePaymentRequired: true } });
  await h.pay();
  await h.pay();
  assert.equal(h.paths('/pay/card').length, 1);
  assert.equal(h.element('pay').disabled, true);
  await h.element('buy-now-check').onclick();
  assert.equal(reconciled, true);
  assert.deepEqual(h.shown, [31]);
});

test('confirmed refund never shows payment success', async () => {
  const h = await harness(async (route) => {
    if (route === 'buyer/immediate-checkouts')
      return { id: 's1', expiresAt: '2999-01-01T00:00:00Z', quote: { total: 12.1 } };
    return { status: 'Failed', stage: 'Refunded', errorCode: 'CheckoutChanged' };
  }, { product: { immediatePaymentRequired: true } });
  await h.pay();
  assert.ok(h.messages.at(-1).value.includes('hoàn tiền'));
  assert.deepEqual(h.shown, []);
});

test('continuing from the cart leaves Buy It Now and quotes the cart again', async () => {
  const h = await harness(async (route) => {
    if (route === 'cart/quote') return { subtotal: 0, discount: 0, shipping: 0, total: 0, sellers: [] };
    throw new Error(route);
  });
  h.element('continue').onclick();
  await tick();
  assert.ok(!h.location.href.includes('product='));
  assert.equal(h.element('cart-method-paypal').closest().hidden, false);
  assert.ok(!h.element('checkout-items').innerHTML.includes('buy-now-item'));
});
