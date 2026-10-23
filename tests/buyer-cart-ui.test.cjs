const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

async function harness(handler, method = 'card') {
  const elements = new Map(),
    messages = [],
    redirects = [],
    calls = [];
  const element = (id) => {
    if (!elements.has(id))
      elements.set(id, {
        value:
          id === 'address'
            ? '1'
            : id === 'count'
              ? '3'
              : id === 'card-number'
                ? '4111111111111111'
                : id === 'card-expiry'
                  ? '12/30'
                  : '',
        disabled: false,
        hidden: false,
        innerHTML: '',
        textContent: '',
        dataset: {},
        classList: { toggle() {}, add() {}, remove() {} },
        setAttribute() {},
        addEventListener() {},
        querySelectorAll() {
          return [];
        },
        focus() {},
      });
    return elements.get(id);
  };
  const controls = (selector) => {
    const result = [];
    for (const match of (element('cart').innerHTML + element('checkout-items').innerHTML).matchAll(
      /<(?:input|select|button)[^>]*\bdata-(quantity|remove|seller-coupon|apply-coupon)="([^"]+)"[^>]*>/g,
    )) {
      const camel = match[1].replace(/-([a-z])/g, (_, x) => x.toUpperCase());
      if (!selector.includes('[data-' + match[1] + ']')) continue;
      const control = element(match[1] + '-' + match[2]);
      control.dataset[camel] = match[2];
      result.push(control);
    }
    return result;
  };
  const noop = () => {};
  const G4 = {
    $: element,
    money: (x) => '$' + Number(x || 0).toFixed(2),
    esc: (x) =>
      String(x ?? '')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;'),
    api: async (route, verb, body) => {
      calls.push({ route, body });
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
      return handler(route, verb, body);
    },
    safe: (fn) => fn(),
    message: (value, success) => messages.push({ value, success }),
    productThumbnail: () => '',
    Promotions: { linePriceHtml: () => '', breakdownHtml: () => '' },
  };
  const context = {
    G4,
    crypto: { randomUUID: () => Math.random().toString() },
    location: { assign: (url) => redirects.push(url) },
    document: { querySelectorAll: controls, querySelector: () => ({ value: method }) },
  };
  G4.PayPalCart = {
    begin: async (checkoutKey) => {
      const result = await G4.api('buyer/paypal-checkouts', 'POST', {
        checkoutKey,
        key: 'cart-payment-key',
      });
      if (result.approvalUrl) context.location.assign(result.approvalUrl);
    },
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/buyer-addresses.js', 'utf8'), context);
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/buyer-cart.js', 'utf8'), context);
  await G4.BuyerCart.init({ tab: noop, loadOrders: async () => {}, showOrder: async () => {} });
  return { element, messages, redirects, calls, controls };
}
const items = [
  {
    productId: 1,
    title: 'Item A',
    price: 10,
    quantity: 1,
    available: 5,
    sellerId: 2,
    sellerName: '<Shop A>',
    weightKg: 1,
  },
  {
    productId: 2,
    title: 'Item B',
    price: 20,
    quantity: 1,
    available: 5,
    sellerId: 3,
    sellerName: 'Shop B',
    weightKg: 1,
  },
];
const cart = { version: 'v1', items };
const quoted = {
  subtotal: 30,
  discount: 0,
  shipping: 7,
  total: 37,
  totalWeightKg: 2,
  sellers: [
    {
      sellerId: 2,
      quote: {
        subtotal: 10,
        discount: 0,
        shipping: 2,
        total: 12,
        totalWeightKg: 1,
        pricingFingerprint: 'fp-a',
        shippingBase: 2,
        shippingDiscount: 0,
        promotions: [],
      },
    },
    {
      sellerId: 3,
      quote: {
        subtotal: 20,
        discount: 0,
        shipping: 5,
        total: 25,
        totalWeightKg: 1,
        pricingFingerprint: 'fp-b',
        shippingBase: 5,
        shippingDiscount: 0,
        promotions: [],
      },
    },
  ],
};

test('restores saved buyer cart grouped by seller with escaped names', async () => {
  const h = await harness(async (route) => (route === 'cart' ? cart : quoted));
  assert.match(h.element('cart').innerHTML, /&lt;Shop A&gt;/);
  assert.match(h.element('cart').innerHTML, /Shop B/);
  assert.match(h.element('checkout-items').innerHTML, /\$12.00/);
  assert.match(h.element('checkout-items').innerHTML, /\$25.00/);
  assert.equal(h.element('total').textContent, '$37.00');
});

test('submits viewed total and fingerprint per seller and invalidates rejected quote', async () => {
  let submitted;
  const h = await harness(async (route, verb, body) => {
    if (route === 'cart') return cart;
    if (route === 'cart/quote') return quoted;
    if (route === 'cart/checkout') {
      submitted = body;
      const error = new Error('Báo giá đã thay đổi');
      error.status = 409;
      throw error;
    }
    throw new Error(route);
  });
  await assert.rejects(h.element('pay').onclick(), /cập nhật báo giá/);
  assert.equal(submitted.cartVersion, 'v1');
  assert.equal(submitted.sellers[0].expectedTotal, 12);
  assert.equal(submitted.sellers[0].pricingFingerprint, 'fp-a');
  assert.equal(submitted.sellers[1].expectedTotal, 25);
  assert.equal(submitted.sellers[1].pricingFingerprint, 'fp-b');
  assert.equal(h.element('pay').disabled, true);
});

test('freezes cart and coupon inputs while checkout is being created', async () => {
  let release;
  const h = await harness(async (route) => {
    if (route === 'cart') return cart;
    if (route === 'cart/quote') return quoted;
    if (route === 'cart/checkout') {
      await new Promise((resolve) => (release = resolve));
      throw new Error('Mất kết nối');
    }
  });
  const paying = h.element('pay').onclick();
  assert.match(h.element('address-list').innerHTML, /disabled/);
  assert.equal(h.element('address-form-save').disabled, true);
  assert.equal(h.element('random').disabled, true);
  assert.equal(h.element('seller-coupon-2').disabled, true);
  assert.equal(h.element('quantity-1').disabled, true);
  release();
  await assert.rejects(paying);
  assert.doesNotMatch(h.element('address-list').innerHTML, /disabled/);
  assert.equal(h.element('address-form-save').disabled, false);
  assert.equal(h.element('seller-coupon-2').disabled, false);
});

test('retries uncertain checkout with same batch key and pricing snapshot', async () => {
  const attempts = [];
  const h = await harness(async (route, verb, body) => {
    if (route === 'cart') return cart;
    if (route === 'cart/quote') return quoted;
    if (route === 'cart/checkout') {
      attempts.push(JSON.stringify(body));
      throw new Error('Mất kết nối');
    }
  });
  await assert.rejects(h.element('pay').onclick());
  await assert.rejects(h.element('pay').onclick());
  assert.equal(attempts.length, 2);
  assert.equal(attempts[0], attempts[1]);
});

test('saves removal with cart version and re-quotes remaining seller', async () => {
  let saved;
  const h = await harness(async (route, verb, body) => {
    if (route === 'cart') return cart;
    if (route === 'cart/quote') return quoted;
    if (route === 'cart/save') {
      saved = body;
      return { version: 'v2', items: [items[1]] };
    }
  });
  await h.element('remove-1').onclick();
  assert.equal(saved.version, 'v1');
  assert.equal(saved.items.length, 1);
  assert.equal(saved.items[0].productId, 2);
  assert.doesNotMatch(h.element('cart').innerHTML, /Item A/);
});

test('card payments report partial failure and leave unpaid order visible', async () => {
  let checkedOut = false;
  const paid = [];
  const h = await harness(async (route) => {
    if (route === 'cart') return checkedOut ? { version: 'v2', items: [] } : cart;
    if (route === 'cart/quote') return quoted;
    if (route === 'cart/checkout') {
      checkedOut = true;
      return [
        { id: 11, sellerId: 2, status: 'AwaitingPayment' },
        { id: 12, sellerId: 3, status: 'AwaitingPayment' },
      ];
    }
    if (route.endsWith('/pay/card')) {
      paid.push(route);
      return { status: route.includes('/11/') ? 'Succeeded' : 'Declined' };
    }
  });
  await h.element('pay').onclick();
  assert.equal(paid.length, 2);
  assert.match(h.messages.at(-1).value, /#12/);
  assert.equal(h.messages.at(-1).success, false);
  assert.match(h.element('cart').innerHTML, /Giỏ đang trống/);
});

test('PayPal starts one checkout for the whole cart and preserves the original checkout batch key', async () => {
  let checkedOut = false;
  const h = await harness(async (route) => {
    if (route === 'cart') return checkedOut ? { version: 'v2', items: [] } : cart;
    if (route === 'cart/quote') return quoted;
    if (route === 'cart/checkout') {
      checkedOut = true;
      return [
        { id: 11, status: 'AwaitingPayment' },
        { id: 12, status: 'AwaitingPayment' },
      ];
    }
    if (route === 'buyer/paypal-checkouts')
      return { approvalUrl: 'https://www.sandbox.paypal.com/checkoutnow?token=TEST' };
  }, 'paypal');
  await h.element('pay').onclick();
  assert.equal(h.redirects.length, 1);
  assert.match(h.messages.at(-1).value, /cả giỏ/);
  const call = h.calls.find((x) => x.route === 'buyer/paypal-checkouts');
  assert.equal(
    call.body.checkoutKey,
    h.calls.find((x) => x.route === 'cart/checkout').body.checkoutKey,
  );
  assert.equal(h.calls.filter((x) => x.route.endsWith('/pay/paypal')).length, 0);
});
