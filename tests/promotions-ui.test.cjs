const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
function helpers() {
  const context = {
    G4: {
      esc: (x) =>
        String(x ?? '').replace(
          /[&<>"']/g,
          (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
        ),
      money: (x) => '$' + Number(x || 0).toFixed(2),
      date: (x) => x,
    },
    Date,
    URLSearchParams,
    document: {},
    setTimeout,
    clearTimeout,
  };
  if (fs.existsSync('frontend/wwwroot/js/promotions.js'))
    vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/promotions.js', 'utf8'), context);
  return context.G4.Promotions || {};
}
const base = () => ({
  name: 'Ưu đãi shop',
  type: 'Sale',
  scope: 'all',
  value: '10',
  isPercent: true,
  startAt: '2026-10-04T09:00',
  endAt: '2026-10-05T09:00',
});
test('local scheduling converts to UTC and preserves round trip', () => {
  const p = helpers();
  assert.equal(typeof p.toUtc, 'function');
  const local = '2026-10-04T09:15';
  assert.equal(p.toLocal(p.toUtc(local)), local);
  assert.throws(() => p.toUtc('not-a-date'));
});
test('sale request ignores obsolete hidden coupon limits and never sends ownership', () => {
  const p = helpers();
  assert.equal(typeof p.buildRequest, 'function');
  const result = p.buildRequest(
    {
      ...base(),
      code: 'OLD',
      maxUsage: '0',
      budget: '0',
      fundingSource: 'Platform',
      sellerId: 999,
    },
    'seller',
  );
  assert.equal(result.value, 10);
  assert.equal(result.maxUsage, undefined);
  assert.equal(result.code, undefined);
  assert.equal(result.fundingSource, undefined);
  assert.equal(result.sellerId, undefined);
});
test('coupon normalizes code and blank optional limits remain unlimited', () => {
  const p = helpers();
  assert.equal(typeof p.buildRequest, 'function');
  const r = p.buildRequest(
    {
      ...base(),
      type: 'Coupon',
      code: ' shop_10 ',
      maxUsage: '',
      budget: '',
      maxUsagePerBuyer: '',
      minSubtotal: '50',
    },
    'seller',
  );
  assert.equal(r.code, 'SHOP_10');
  assert.equal(r.budget, null);
  assert.equal(r.maxUsage, null);
  assert.equal(r.minSubtotal, 50);
  assert.throws(
    () => p.buildRequest({ ...base(), type: 'Coupon', code: 'ABC', maxUsage: '0' }, 'seller'),
    /lớn hơn 0/,
  );
});
test('volume tiers require ascending thresholds and nondecreasing discounts', () => {
  const p = helpers();
  assert.equal(typeof p.buildRequest, 'function');
  const input = {
    ...base(),
    type: 'Volume',
    tiers: [
      { minQuantity: '3', percent: '10' },
      { minQuantity: '5', percent: '15' },
    ],
  };
  assert.equal(p.buildRequest(input, 'seller').tiers.length, 2);
  assert.throws(() =>
    p.buildRequest(
      {
        ...input,
        tiers: [
          { minQuantity: 3, percent: 10 },
          { minQuantity: 3, percent: 15 },
        ],
      },
      'seller',
    ),
  );
  assert.throws(() =>
    p.buildRequest(
      {
        ...input,
        tiers: [
          { minQuantity: 3, percent: 15 },
          { minQuantity: 5, percent: 10 },
        ],
      },
      'seller',
    ),
  );
});
test('conditional validations prevent invalid scope, percentage, dates and platform types', () => {
  const p = helpers();
  assert.equal(typeof p.buildRequest, 'function');
  assert.throws(() => p.buildRequest({ ...base(), value: 101 }, 'seller'));
  assert.throws(() => p.buildRequest({ ...base(), endAt: '2026-10-04T08:00' }, 'seller'));
  assert.throws(() => p.buildRequest({ ...base(), scope: 'products', productIds: [] }, 'seller'));
  assert.throws(() => p.buildRequest(base(), 'admin'));
  assert.equal(
    p.buildRequest({ ...base(), type: 'Shipping', freeShipping: true, value: '0' }, 'seller')
      .freeShipping,
    true,
  );
});
test('editing keeps immutable code type and version', () => {
  const p = helpers();
  assert.equal(typeof p.buildRequest, 'function');
  const old = { type: 'Coupon', code: 'FIXED', version: 'AQ==' };
  const r = p.buildRequest({ ...base(), type: 'Sale', code: 'CHANGED' }, 'seller', old);
  assert.equal(r.type, 'Coupon');
  assert.equal(r.code, 'FIXED');
  assert.equal(r.version, 'AQ==');
});
test('quote presentation escapes source labels and does not invent legacy discounts', () => {
  const p = helpers();
  assert.equal(typeof p.breakdownHtml, 'function');
  assert.equal(p.breakdownHtml({ subtotal: 100, discount: 10, shipping: 5, total: 95 }), '');
  const html = p.breakdownHtml({
    shippingBase: 5,
    shippingDiscount: 2,
    promotions: [{ name: '<script>', fundingSource: 'Platform', amount: 10 }],
  });
  assert.match(html, /&lt;script&gt;/);
  assert.match(html, /Nền tảng/);
  assert.match(html, /\$2.00/);
  assert.doesNotMatch(html, /<script>/);
});
test('shipping discount appears once when its promotion already explains it', () => {
  const p = helpers(),
    html = p.breakdownHtml({
      shippingBase: 5,
      shippingDiscount: 2,
      promotions: [{ name: 'Ưu đãi ship', type: 'Shipping', fundingSource: 'Seller', amount: 2 }],
    });
  assert.equal((html.match(/−\$2\.00/g) || []).length, 1);
});
test('line presentation uses server totals without mutating catalog prices', () => {
  const p = helpers();
  assert.equal(typeof p.linePriceHtml, 'function');
  assert.match(
    p.linePriceHtml(
      { productId: 1, originalTotal: 100, sellerDiscount: 10, platformDiscount: 5 },
      100,
      [],
    ),
    /\$85.00/,
  );
  assert.equal(p.linePriceHtml(null, 100, []), '<b>$100.00</b>');
});
test('blank order thresholds omit nonnullable API fields instead of sending null', () => {
  const p = helpers();
  const r = p.buildRequest(
    { ...base(), type: 'Order', minSubtotal: '', minQuantity: '' },
    'seller',
  );
  assert.equal(Object.hasOwn(r, 'minSubtotal'), false);
  assert.equal(Object.hasOwn(r, 'minQuantity'), false);
});
test('editing a platform coupon retains its original shop scope', () => {
  const p = helpers();
  const r = p.buildRequest({ ...base(), type: 'Coupon', code: 'ABC', sellerId: 22 }, 'admin', {
    type: 'Coupon',
    code: 'ABC',
    sellerId: 11,
    version: 1,
  });
  assert.equal(r.sellerId, 11);
});
test('promotion audit is newest first and escapes actor reason and changed name', () => {
  const p = helpers();
  assert.equal(typeof p.auditHtml, 'function');
  const html = p.auditHtml([
    {
      id: 1,
      actorName: 'Older',
      action: 'Create',
      createdAt: '2026-10-04T01:00:00Z',
      changesJson: '{"Name":"Original","Version":1}',
    },
    {
      id: 2,
      actorName: '<Admin>',
      action: 'Edit',
      reason: '<script>alert(1)</script>',
      createdAt: '2026-10-04T02:00:00Z',
      changesJson: '{"Name":"<Sale>","Value":15,"IsPercent":true,"SellerId":99,"Version":2}',
    },
  ]);
  assert.ok(html.indexOf('&lt;Admin&gt;') < html.indexOf('Older'));
  assert.match(html, /Chỉnh sửa/);
  assert.match(html, /Tên khuyến mãi/);
  assert.match(html, /&lt;Sale&gt;/);
  assert.match(html, /15%/);
  assert.match(html, /&lt;script&gt;/);
  assert.doesNotMatch(html, /<script>|SellerId|Version|changesJson|\{"/);
});
test('promotion audit translates lifecycle actions and tolerates legacy malformed changes', () => {
  const p = helpers();
  assert.equal(typeof p.auditHtml, 'function');
  const html = p.auditHtml([
    { id: 2, action: 'Resume', createdAt: '2026-10-04T03:00:00Z', changesJson: 'broken' },
    { id: 1, action: 'Pause', createdAt: '2026-10-04T02:00:00Z', reason: 'Kiểm tra điều kiện' },
  ]);
  assert.match(html, /Kích hoạt lại/);
  assert.match(html, /Tạm dừng/);
  assert.match(html, /Kiểm tra điều kiện/);
  assert.doesNotMatch(html, /broken/);
  assert.match(p.auditHtml([]), /Chưa có lịch sử/);
});
test('audit timestamps without a suffix are treated as UTC database values', () => {
  const html = helpers().auditHtml([{ id: 1, action: 'Create', createdAt: '2026-10-04T03:00:00' }]);
  assert.match(html, />2026-10-04T03:00:00Z<\/time>/);
});
test('admin moderates shop promotions and cannot enable a seller pause', () => {
  const p = helpers(),
    shop = { fundingSource: 'Seller', status: 'Paused', isPaused: true, adminPaused: false };
  const action = p.stateAction(shop, 'admin');
  assert.equal(action.kind, 'suspension');
  assert.equal(action.suspended, true);
  assert.equal(action.label, 'Đình chỉ');
  assert.equal(p.canEdit(shop, 'admin'), false);
});
test('releasing suspension presents a separate admin action and seller must activate', () => {
  const p = helpers(),
    shop = { fundingSource: 'Seller', status: 'Paused', isPaused: true, adminPaused: true };
  assert.equal(p.stateAction(shop, 'admin').label, 'Gỡ đình chỉ');
  assert.equal(p.stateAction(shop, 'admin').suspended, false);
  assert.equal(p.stateAction(shop, 'seller'), null);
  const released = { ...shop, adminPaused: false };
  assert.equal(p.stateAction(released, 'seller').label, 'Kích hoạt lại');
  assert.equal(p.stateAction(released, 'seller').paused, false);
  assert.equal(p.stateAction(released, 'admin').label, 'Đình chỉ');
});
test('admin retains activation rights on platform coupons', () => {
  const p = helpers(),
    coupon = { fundingSource: 'Platform', status: 'Paused', isPaused: true };
  assert.equal(p.stateAction(coupon, 'admin').kind, 'state');
  assert.equal(p.stateAction(coupon, 'admin').paused, false);
  assert.equal(p.canEdit(coupon, 'admin'), true);
  assert.equal(p.canEdit(coupon, 'seller'), false);
  assert.equal(p.stateAction(coupon, 'seller'), null);
});
test('audit uses clear suspension and release labels', () => {
  const html = helpers().auditHtml([
    { id: 1, action: 'Suspend', reason: 'Vi phạm', createdAt: '2026-10-13T01:00:00' },
    {
      id: 2,
      action: 'ReleaseSuspension',
      reason: 'Đã khắc phục',
      createdAt: '2026-10-13T02:00:00',
    },
  ]);
  assert.match(html, /Đình chỉ/);
  assert.match(html, /Gỡ đình chỉ/);
  assert.match(html, /Đã khắc phục/);
});
