const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const baseQuote = {
  orderId: 7,
  reasonCode: 'WrongItem',
  goodsPaid: 90,
  outboundShippingRetained: 0,
  returnShippingFee: 5,
  buyerReturnShippingFee: 0,
  buyerRefund: 95,
  platformSubsidyReversal: 10,
  shippingPayer: 'Seller',
  canRequest: true,
  pricingFingerprint: 'a'.repeat(64),
};
function load(api = async () => baseQuote) {
  const nodes = new Map();
  const node = (id) => {
    if (!nodes.has(id))
      nodes.set(id, {
        value: '',
        open: false,
        setAttribute(name, value) {
          this[name] = value;
        },
        focus() {
          this.focused = true;
        },
        showModal() {
          this.open = true;
        },
        close() {
          this.open = false;
        },
      });
    return nodes.get(id);
  };
  let key = 0;
  const context = {
    G4: {
      $: node,
      api,
      esc: (x) =>
        String(x ?? '').replace(
          /[&<>"']/g,
          (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
        ),
      money: (x) => '$' + Number(x).toFixed(2),
      message() {},
    },
    crypto: { randomUUID: () => 'key-' + ++key },
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/returns.js', 'utf8'), context);
  context.G4.Returns.init();
  return { ui: context.G4.Returns, node };
}

test('return deadline uses UTC, expires independently of receipt and preserves timely case guidance', () => {
  const { ui } = load();
  assert.equal(ui.windowOpen({ returnWindowClosesAt: '2070-01-01T00:00:00' }), true);
  const future = ui.windowHtml({ returnWindowClosesAt: '2070-01-01T00:00:00' });
  assert.match(future, /datetime="2070-01-01T00:00:00Z"/);
  assert.match(future, /Gửi yêu cầu trước hạn/);
  const past = {
    returnWindowClosesAt: '2000-01-01T00:00:00Z',
    returnRequest: { status: 'Requested' },
  };
  assert.equal(ui.windowOpen(past), false);
  assert.match(
    ui.windowHtml(past),
    /Đã hết hạn.*tiếp tục được xử lý.*hạn kiểm tra hàng trả là riêng/,
  );
});

test('a quote that expires before submit cannot post a new return', async () => {
  let posts = 0;
  const { ui, node } = load(async (url, method) => {
    if (method === 'POST') posts++;
    return { ...baseQuote, returnWindowClosesAt: '2000-01-01T00:00:00Z' };
  });
  await ui.open(7);
  await node('return-form').onsubmit({ preventDefault() {} });
  assert.equal(posts, 0);
  assert.equal(node('return-confirm').disabled, true);
  assert.match(node('return-error').textContent, /hết hạn/);
  assert.doesNotMatch(node('return-quote').innerHTML, /Phí trả hàng vượt/);
});
test('preview shows buyer-funded return deduction and excludes platform subsidy from cash', () => {
  const { ui } = load();
  const html = ui.quoteHtml({
    ...baseQuote,
    reasonCode: 'ChangedMind',
    shippingPayer: 'Buyer',
    outboundShippingRetained: 5,
    buyerReturnShippingFee: 5,
    buyerRefund: 85,
  });
  assert.match(html, /trừ vào khoản hoàn/);
  assert.match(html, /không hoàn/);
  assert.match(html, /\$85.00/);
  assert.match(html, /không cộng thêm/);
  assert.doesNotMatch(html, /\$105.00/);
});
test('blocked excessive postage displays explanation and disables confirmation', async () => {
  const { ui, node } = load(async () => ({
    ...baseQuote,
    canRequest: false,
    blockReason: '<img> Phí quá lớn',
    buyerRefund: 0,
  }));
  await ui.open(7);
  assert.equal(node('return-confirm').disabled, true);
  assert.match(node('return-quote').innerHTML, /&lt;img&gt;/);
  assert.doesNotMatch(node('return-quote').innerHTML, /<img>/);
  assert.doesNotMatch(node('return-quote').innerHTML, /Bạn được hoàn/);
});
test('saved pricing escapes description and only claims a completed refund from Succeeded', () => {
  const { ui } = load();
  const order = {
    returnRequest: {
      id: 1,
      pricingPolicyVersion: 1,
      shippingPayer: 'Buyer',
      returnShippingFee: 5,
      outboundShippingRetained: 5,
      buyerRefundAmount: 85,
      description: '<script>unsafe</script>',
    },
    refunds: [{ returnRequestId: 1, status: 'Failed', amount: 85 }],
  };
  let html = ui.savedHtml(order);
  assert.match(html, /&lt;script&gt;/);
  assert.doesNotMatch(html, /Đã hoàn thành công/);
  order.refunds[0].status = 'Succeeded';
  html = ui.savedHtml(order);
  assert.match(html, /Đã hoàn thành công/);
});
test('reason changes ignore stale quote responses', async () => {
  let oldResponse;
  const { ui, node } = load((path) =>
    path.includes('WrongItem')
      ? new Promise((resolve) => {
          oldResponse = resolve;
        })
      : Promise.resolve({
          ...baseQuote,
          reasonCode: 'ChangedMind',
          shippingPayer: 'Buyer',
          buyerRefund: 85,
        }),
  );
  const opening = ui.open(7);
  node('return-reason').value = 'ChangedMind';
  await node('return-reason').onchange();
  oldResponse(baseQuote);
  await opening;
  assert.match(node('return-quote').innerHTML, /\$85.00/);
  assert.doesNotMatch(node('return-quote').innerHTML, /\$95.00/);
});
test('uncertain submit freezes form and retries same key without sending client prices', async () => {
  const posted = [];
  const { ui, node } = load(async (path, method, data) => {
    if (method === 'POST') {
      posted.push(data);
      throw new Error('Uncertain response');
    }
    return baseQuote;
  });
  await ui.open(7);
  node('return-description').value = 'Description';
  const pending = node('return-form').onsubmit({ preventDefault() {} });
  assert.equal(node('return-fields').disabled, true);
  await pending;
  assert.equal(node('return-fields').disabled, false);
  assert.equal(node('return-error').focused, true);
  await node('return-form').onsubmit({ preventDefault() {} });
  assert.equal(posted[0].key, posted[1].key);
  assert.equal(posted[0].pricingFingerprint, baseQuote.pricingFingerprint);
  assert.deepEqual(Object.keys(posted[0]).sort(), [
    'description',
    'key',
    'pricingFingerprint',
    'reasonCode',
  ]);
});
test('closed dialog never gets overwritten by a late quote', async () => {
  let response;
  const { ui, node } = load(
    () =>
      new Promise((resolve) => {
        response = resolve;
      }),
  );
  const opening = ui.open(7);
  node('return-close').onclick();
  response(baseQuote);
  await opening;
  assert.equal(node('return-dialog').open, false);
  assert.notEqual(node('return-confirm').disabled, false);
});
