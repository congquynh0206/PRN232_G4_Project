const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function harness(name, api = async () => ({})) {
  const nodes = new Map();
  const node = (id) => {
    if (!nodes.has(id))
      nodes.set(id, {
        value: '',
        innerHTML: '',
        textContent: '',
        disabled: false,
        setAttribute(k, v) {
          this[k] = v;
        },
        addEventListener() {},
        focus() {
          this.focused = true;
        },
        showModal() {
          this.open = true;
        },
        close() {
          this.open = false;
        },
        reset() {},
      });
    return nodes.get(id);
  };
  const context = {
    G4: {
      $: node,
      api,
      esc: (s) =>
        String(s ?? '').replace(
          /[<>&"']/g,
          (c) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&#39;' })[c],
        ),
      date: (s) => s || '',
      pager() {},
      productThumbnail: () => '',
      message() {},
    },
    crypto: { randomUUID: () => 'stable-request-key' },
    URLSearchParams,
    Date,
    document: { querySelectorAll: () => [] },
  };
  vm.createContext(context);
  vm.runInContext(fs.readFileSync(`frontend/wwwroot/js/${name}.js`, 'utf8'), context);
  return { ui: context.G4, node };
}
function formEvent(kind, id, comment = ' Thanks ', rating = '5') {
  const fieldset = {},
    button = {},
    alert = {
      focus() {
        this.focused = true;
      },
    },
    textarea = { value: comment },
    select = { value: rating };
  const form = {
    dataset: { feedbackReply: String(id), productReview: String(id) },
    querySelector: (s) =>
      ({
        fieldset,
        'button[type=submit]': button,
        button,
        '[role=alert]': alert,
        textarea,
        select,
      })[s],
    setAttribute() {},
  };
  return {
    event: { target: { closest: (s) => (s.includes(kind) ? form : null) }, preventDefault() {} },
    form,
    fieldset,
    button,
    alert,
    textarea,
    select,
  };
}
test('product feedback escapes customer, product and reply text; legacy review never claims verified purchase', () => {
  const { ui } = harness('product-feedback');
  const review = ui.ProductFeedback.reviewHtml({
    id: 5,
    buyerName: '<script>',
    rating: 3,
    comment: '<img>',
    verifiedPurchase: false,
    reply: { comment: '<svg>', createdAt: 'now' },
  });
  assert.doesNotMatch(review, /<script>|<img>|<svg>|Đã mua hàng/);
  assert.match(review, /&lt;script&gt;/);
  assert.match(review, /&lt;img&gt;/);
  assert.match(review, /&lt;svg&gt;/);
  assert.doesNotMatch(review, /data-feedback-reply/);
  assert.match(
    ui.ProductFeedback.productHtml({
      productId: 4,
      title: '<img>',
      reviewCount: 2,
      averageRating: 4,
      unansweredCount: 1,
    }),
    /&lt;img&gt;/,
  );
});
test('seller reply with uncertain result freezes text, retries the exact key/payload and does not refresh away the form', async () => {
  const calls = [];
  const { ui } = harness('product-feedback', async (path, method, body) => {
    calls.push({ path, method, body: JSON.parse(JSON.stringify(body)) });
    throw new Error('connection lost');
  });
  const f = formEvent('feedback-reply', 6);
  await ui.ProductFeedback.submit(f.event);
  assert.equal(f.fieldset.disabled, true);
  assert.equal(f.button.disabled, false);
  f.textarea.value = 'changed';
  await ui.ProductFeedback.submit(f.event);
  assert.deepEqual(calls[0], calls[1]);
  assert.equal(calls[0].body.comment, 'Thanks');
  assert.match(f.alert.textContent, /Kết quả chưa rõ/);
});
test('seller reply rejects whitespace before API call and unlocks after a definitive conflict', async () => {
  let calls = 0;
  const { ui } = harness('product-feedback', async () => {
    calls++;
    throw Object.assign(new Error('Đã được trả lời'), { status: 409 });
  });
  const f = formEvent('feedback-reply', 6, ' ');
  await ui.ProductFeedback.submit(f.event);
  assert.equal(calls, 0);
  f.textarea.value = 'valid';
  await ui.ProductFeedback.submit(f.event);
  assert.equal(calls, 1);
  assert.equal(f.fieldset.disabled, false);
  assert.equal(f.alert.focused, true);
});
test('seller filters reject reversed dates; include entire final local day using an exclusive bound', () => {
  const { ui, node } = harness('product-feedback');
  node('product-feedback-from').value = '2026-10-18';
  node('product-feedback-to').value = '2026-10-17';
  assert.throws(() => ui.ProductFeedback.readFilters(), /Ngày bắt đầu/);
  node('product-feedback-from').value = '2026-10-17';
  const filters = ui.ProductFeedback.readFilters();
  assert.equal(new Date(filters.get('to')) - new Date(filters.get('from')), 86400000);
});
test('a late seller product request cannot overwrite the newer product selection and review list', async () => {
  let resolveOld;
  const result = (id, title) => ({
    page: 1,
    pageSize: 8,
    totalCount: 1,
    summary: { productCount: 1, reviewCount: 1, averageRating: 4, unansweredCount: 1 },
    items: [{ productId: id, title, reviewCount: 1, averageRating: 4, unansweredCount: 1 }],
  });
  let calls = 0;
  const { ui, node } = harness('product-feedback', async (path) => {
    if (path.includes('/reviews?'))
      return { title: 'New product', page: 1, pageSize: 10, totalCount: 0, items: [] };
    if (++calls === 1)
      return new Promise((resolve) => {
        resolveOld = resolve;
      });
    return result(2, 'New product');
  });
  const old = ui.ProductFeedback.load();
  await ui.ProductFeedback.load();
  resolveOld(result(1, 'Old product'));
  await old;
  assert.match(node('product-feedback-products').innerHTML, /New product/);
  assert.doesNotMatch(node('product-feedback-products').innerHTML, /Old product/);
  assert.equal(node('product-feedback-review-title').textContent, 'New product');
});
test('buyer sees escaped seller reply and submits only server-linked order/product with a stable retry payload', async () => {
  const calls = [];
  const { ui } = harness('buyer-product-reviews', async (path, method, body) => {
    if (!method) return { items: [] };
    calls.push({ path, body: JSON.parse(JSON.stringify(body)) });
    throw new Error('unknown result');
  });
  await ui.BuyerProductReviews.open(9);
  const html = ui.BuyerProductReviews.itemHtml({
    productId: 3,
    title: '<img>',
    review: { rating: 5, comment: '<script>', reply: { comment: '<svg>' } },
  });
  assert.doesNotMatch(html, /<img>|<script>|<svg>|data-product-review=/);
  assert.match(html, /Shop trả lời/);
  const f = formEvent('product-review', 3, ' Great ', '5');
  await ui.BuyerProductReviews.submit(f.event);
  f.textarea.value = 'changed';
  f.select.value = '1';
  await ui.BuyerProductReviews.submit(f.event);
  assert.deepEqual(calls[0], calls[1]);
  assert.equal(calls[0].path, 'orders/9/products/3/review');
  assert.equal(calls[0].body.rating, 5);
  assert.equal(calls[0].body.comment, 'Great');
});
