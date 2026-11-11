const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const tick = () => new Promise((resolve) => setImmediate(resolve));

async function harness(handler = async () => ({}), overrides = {}) {
  const elements = new Map();
  const calls = [];
  const visits = [];
  const element = (id) => {
    if (!elements.has(id))
      elements.set(id, {
        value: '',
        hidden: false,
        disabled: false,
        innerHTML: '',
        textContent: '',
        listeners: {},
        addEventListener(event, fn) {
          this.listeners[event] = fn;
        },
        setAttribute() {},
      });
    return elements.get(id);
  };
  const product = {
    id: 1,
    title: '<img onerror=attack()>',
    price: 10,
    available: 1,
    immediatePaymentRequired: true,
    sellerName: '<Shop>',
    allowOffers: true,
    ...overrides,
  };
  const G4 = {
    $: element,
    money: (x) => '$' + Number(x || 0).toFixed(2),
    esc: (s) => String(s || '').replaceAll('<', '&lt;').replaceAll('>', '&gt;'),
    productThumbnail: () => '',
    api: async (path, method, body) => {
      calls.push({ path, method, body });
      if (path === 'catalog/products') return [product];
      return handler(path, method, body);
    },
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/buyer-products.js', 'utf8'), {
    window: { G4 },
    location: { assign: (url) => visits.push(url) },
  });
  await tick();
  return { element, calls, visits };
}

const clickOn = (data) => ({
  target: {
    closest: (selector) =>
      selector === `[data-${Object.keys(data)[0].replace(/[A-Z]/g, (c) => '-' + c.toLowerCase())}]`
        ? { dataset: data, disabled: false }
        : null,
  },
});

test('product list escapes data and Buy It Now opens the checkout tab', async () => {
  const h = await harness();
  const html = h.element('products-list').innerHTML;
  assert.ok(html.includes('&lt;img'));
  assert.ok(html.includes('&lt;Shop&gt;'));
  assert.ok(html.includes('Yêu cầu thanh toán ngay'));
  assert.ok(!html.includes('<img'));
  // Immediate products cannot enter the cart; Buy It Now and Make Offer stay available.
  assert.match(html, /data-add-cart="1"\s+disabled/);
  assert.ok(html.includes('/Buyer?tab=best-offers&offer=1'));
  h.element('products-list').listeners.click(clickOn({ buy: '1' }));
  assert.deepEqual(h.visits, ['/Buyer?tab=checkout&product=1#checkout']);
  assert.equal(h.calls.some((x) => x.method === 'POST'), false);
});

test('add to cart saves the current cart plus one unit and reports it', async () => {
  const h = await harness(async (path, method, body) => {
    if (path === 'cart') return { version: 'v1', items: [{ productId: 5, quantity: 2 }] };
    if (path === 'cart/save') return { version: 'v2', items: body.items };
    throw new Error(path);
  }, { immediatePaymentRequired: false, allowOffers: false });
  assert.ok(!h.element('products-list').innerHTML.includes('offer='));
  h.element('products-list').listeners.click(clickOn({ addCart: '1' }));
  for (let i = 0; i < 4; i++) await tick();
  const save = h.calls.find((x) => x.path === 'cart/save');
  assert.equal(save.body.version, 'v1');
  assert.equal(
    JSON.stringify(save.body.items),
    JSON.stringify([{ productId: 5, quantity: 2 }, { productId: 1, quantity: 1 }]),
  );
  assert.ok(h.element('message').innerHTML.includes('Đã thêm'));
});
