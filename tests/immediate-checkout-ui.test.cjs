const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const tick = () => new Promise((resolve) => setImmediate(resolve));
async function harness(handler = async () => ({}), search = '') {
  const elements = new Map();
  const calls = [];
  const element = (id) => {
    if (!elements.has(id)) elements.set(id, {
      value: id === 'buy-quantity' || id === 'buy-address' ? '1' : '',
      hidden: false, disabled: false, innerHTML: '', textContent: '',
      listeners: {},
      addEventListener(event, fn) { this.listeners[event] = fn; },
      scrollIntoView() {},
    });
    return elements.get(id);
  };
  const product = {
    id: 1, title: '<img onerror=attack()>', description: '<script>attack()</script>',
    price: 10, available: 1, immediatePaymentRequired: true,
  };
  const G4 = {
    $: element,
    money: (x) => '$' + Number(x || 0).toFixed(2),
    esc: (s) => String(s || '').replaceAll('<', '&lt;').replaceAll('>', '&gt;'),
    api: async (path, method, body) => {
      calls.push({ path, method, body });
      if (path === 'catalog/products') return [product];
      if (path === 'addresses') return [{ id: 1, fullName: 'QA', isDefault: true }];
      if (path === 'buyer/immediate-checkouts') return {
        id: 'session', title: product.title,
        quote: { subtotal: 10, discount: 0, shipping: 2.1, total: 12.1 },
      };
      return handler(path, method, body);
    },
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/immediate-checkout.js', 'utf8'), {
    window: { G4 }, crypto: { randomUUID: () => Math.random().toString() },
    location: { search, assign() {} }, history: { replaceState() {} }, URLSearchParams,
  });
  await tick();
  const submit = (id) => element(id).listeners.submit({ preventDefault() {} });
  async function checkout() {
    element('products-list').listeners.click({
      target: { closest: () => ({ dataset: { buy: '1' } }) },
    });
    await submit('buy-now-form');
  }
  return { element, calls, submit, checkout };
}

test('catalog escapes product data and checkout calls no normal order or payment API', async () => {
  const h = await harness();
  assert.ok(h.element('products-list').innerHTML.includes('&lt;img'));
  assert.ok(h.element('products-list').innerHTML.includes('Immediate Payment Required'));
  await h.checkout();
  assert.equal(h.calls.filter((x) => x.method === 'POST').length, 1);
  assert.equal(h.calls.at(-1).path, 'buyer/immediate-checkouts');
  assert.ok(h.element('checkout-quote').innerHTML.includes('$12.10'));
  assert.equal(h.element('immediate-payment').hidden, false);
});

test('double Pay Now submits one payment and shows success with order history link', async () => {
  let resolve;
  const h = await harness(() => new Promise((r) => { resolve = r; }));
  await h.checkout();
  const first = h.submit('pay-now-form');
  await h.submit('pay-now-form');
  assert.equal(h.calls.filter((x) => x.path.endsWith('/pay/card')).length, 1);
  assert.equal(h.element('pay-now').disabled, true);
  resolve({ status: 'Succeeded', orderId: 42 });
  await first;
  assert.equal(h.element('payment-success').hidden, false);
  assert.ok(h.element('paid-order').textContent.includes('#42'));
  assert.equal(h.element('card-number').value, '');
});

test('HTTP uncertainty blocks payment until server status recovery', async () => {
  const h = await harness(async (path) => {
    if (path.endsWith('/pay/card')) throw new Error('Network lost');
    return { status: 'Succeeded', orderId: 77 };
  });
  await h.checkout();
  await h.submit('pay-now-form');
  await h.submit('pay-now-form');
  assert.equal(h.calls.filter((x) => x.path.endsWith('/pay/card')).length, 1);
  assert.equal(h.element('pay-now').disabled, true);
  await h.element('check-payment').listeners.click();
  await tick();
  assert.equal(h.element('payment-success').hidden, false);
});

test('refresh of successful checkout reads status without charging again', async () => {
  const h = await harness(async () => ({ status: 'Succeeded', orderId: 9 }), '?checkout=session');
  assert.equal(h.element('payment-success').hidden, false);
  assert.equal(h.calls.some((x) => x.method === 'POST'), false);
});

test('declined attempt permits retry with new key; out of stock is explicit', async () => {
  let count = 0;
  const h = await harness(async () => {
    if (++count === 1) return { status: 'Failed', errorCode: 'Declined' };
    throw Object.assign(new Error('Out of Stock'), { code: 'OutOfStock' });
  });
  await h.checkout();
  await h.submit('pay-now-form');
  assert.ok(h.element('message').textContent.includes('Declined'));
  await h.submit('pay-now-form');
  const keys = h.calls.filter((x) => x.path.endsWith('/pay/card')).map((x) => x.body.key);
  assert.notEqual(keys[0], keys[1]);
  assert.ok(h.element('message').textContent.includes('Out of Stock'));
  assert.equal(h.element('immediate-payment').hidden, true);
});

test('editing checkout invalidates the quote before a payment can be submitted', async () => {
  const h = await harness();
  await h.checkout();
  h.element('buy-quantity').listeners.input();
  await h.submit('pay-now-form');
  assert.equal(h.element('immediate-payment').hidden, true);
  assert.equal(h.calls.some((x) => x.path.endsWith('/pay/card')), false);
});

test('restoring an expired session blocks Pay Now and requires a new quote', async () => {
  const h = await harness(async () => ({
    status: 'Expired',
    snapshot: { title: 'Expired', quantity: 1, quote: { total: 12.1 } },
  }), '?checkout=session');
  assert.equal(h.element('pay-now').disabled, true);
  await h.submit('pay-now-form');
  assert.equal(h.calls.some((x) => x.method === 'POST'), false);
});

test('pending capture blocks a second payment and explicitly reconciles before success', async () => {
  let reconciled = false;
  const h = await harness(async (path) => {
    if (path.endsWith('/pay/card')) return { status: 'Verifying', stage: 'Capturing' };
    if (path.endsWith('/reconcile')) {
      reconciled = true;
      return { status: 'Succeeded', orderId: 31 };
    }
    return reconciled ? { status: 'Succeeded', orderId: 31 } : {
      status: 'Verifying', attempt: { status: 'Verifying', key: 'existing-key' },
      snapshot: { title: 'Item', quantity: 1, quote: { total: 12.1 } },
    };
  });
  await h.checkout();
  await h.submit('pay-now-form');
  await h.submit('pay-now-form');
  assert.equal(h.calls.filter((x) => x.path.endsWith('/pay/card')).length, 1);
  assert.equal(h.element('pay-now').disabled, true);
  await h.element('check-payment').listeners.click();
  await tick();
  assert.equal(reconciled, true);
  assert.equal(h.element('payment-success').hidden, false);
});

test('unconfirmed void never displays a completed void or allows a new payment', async () => {
  const h = await harness(async () => ({
    status: 'Verifying', stage: 'Voiding', errorCode: 'OutOfStock',
  }));
  await h.checkout();
  await h.submit('pay-now-form');
  assert.equal(h.element('pay-now').disabled, true);
  assert.equal(h.element('check-payment').hidden, false);
  assert.equal(h.element('payment-success').hidden, true);
  assert.ok(!h.element('message').textContent.includes('đã được Void'));
});

test('confirmed compensation shows refund and never shows payment success', async () => {
  const h = await harness(async () => ({
    status: 'Failed', stage: 'Refunded', errorCode: 'CheckoutChanged',
  }));
  await h.checkout();
  await h.submit('pay-now-form');
  assert.ok(h.element('message').textContent.includes('hoàn tiền'));
  assert.equal(h.element('immediate-payment').hidden, true);
});
