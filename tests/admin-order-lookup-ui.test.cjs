const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const tick = () => new Promise(resolve => setImmediate(resolve));
const row = (id = 100, orderDate = '2026-10-19T08:00:00') => ({ orderId: id, orderDate, buyer: '<Buyer>', seller: 'Shop', total: 100, orderStatus: 'Paid', paymentStatus: 'Thành công · có lần lỗi', hasPaymentFailure: true, hasRefund: false, hasDispute: false });
const result = (items = [row()], page = 1) => ({ page, pageSize: 20, totalCount: items.length, items });
const detail = (id = 100) => ({ order: row(id), collectedAmount: 100, refundedAmount: 20, remainingAmount: 80, items: [{ title: '<script>bad</script>', quantity: 1, unitPrice: 100 }], payments: [{ paymentId: 1, method: 'PayPal', status: 'Succeeded', amount: 100, createdAt: '2026-10-19T08:00:00', paidAt: '2026-10-19T08:00:00Z', errorCode: '<error>' }], refunds: [{ refundId: 1, paymentId: 1, amount: 20, currency: 'USD', status: 'Succeeded', reason: 'shipping-adjustment', createdAt: '2026-10-19T08:00:00', completedAt: '2026-10-19T15:00:00+07:00' }], shipments: [{ shipmentId: 1, direction: 'Return', status: 'Delivered', createdAt: '2026-10-19T08:00:00', deliveredAt: '2026-10-19T08:00:00Z', events: [{ status: 'Delivered', occurredAt: '2026-10-19T08:00:00' }] }], disputes: [{ disputeId: 1, status: 'Escalated', createdAt: '2026-10-19T08:00:00' }] });
function harness(handler = async route => route.includes('?') ? result() : detail()) {
  const elements = new Map(), calls = [], dates = [], bodyEvents = {}, formValues = { searchType: 'Auto' };
  let html = '', pagination;
  function $(id) {
    if (!elements.has(id)) elements.set(id, { id, value: '', innerHTML: '', textContent: '', hidden: false, dataset: {}, attrs: {}, events: {}, tagName: 'INPUT', focus() {}, setAttribute(k, v) { this.attrs[k] = v; }, addEventListener(k, fn) { this.events[k] = fn; } });
    return elements.get(id);
  }
  const form = $('order-lookup-filters');
  form.elements = { namedItem: key => ({ value: formValues[key] || '', tagName: 'INPUT' }) };
  form.reset = () => { for (const key of Object.keys(formValues)) delete formValues[key]; formValues.searchType = 'Auto'; $('lookup-search-type').value = 'Auto'; };
  $('lookup-search-type').value = 'Auto';
  const quick = ['', 'AwaitingPayment', 'PaymentFailed', 'HasRefund', 'HasDispute'].map(key => { const button = $('quick-' + key); button.dataset.lookupQuick = key; return button; });
  const G4 = { $, esc: value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]), money: value => '$' + Number(value || 0).toFixed(2), label: value => value,
    shippingStatus: (value, direction) => `${direction || 'Outbound'}:${value}`, safe: fn => fn(),
    date: value => { dates.push(value); return new Date(value).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour12: false }); },
    api: async route => { calls.push(route); return handler(route); }, pager: (id, page, fn) => { pagination = fn; }, openDetail: value => { html = value; }, closeDetail() {} };
  class FormDataStub { entries() { return Object.entries(formValues)[Symbol.iterator](); } }
  const document = { querySelector: selector => selector === '.order-lookup-table-wrap' ? $('table-wrap') : null, querySelectorAll: selector => selector === '[data-lookup-quick]' ? quick : [], body: { addEventListener: (event, fn) => { bodyEvents[event] = fn; } } };
  const context = { window: { G4 }, G4, document, URLSearchParams, FormData: FormDataStub, Intl, Date };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/admin-order-lookup.js', 'utf8'), context);
  G4.AdminOrderLookup.init();
  return { $, calls, dates, formValues, ui: G4.AdminOrderLookup, html: () => html, quick,
    submit: async () => { form.events.submit({ preventDefault() {} }); await tick(); },
    paginate: p => pagination(p),
    click: id => bodyEvents.click({ target: { closest: selector => selector === '[data-open-admin-order]' ? { dataset: { openAdminOrder: String(id) } } : null } }),
    close: () => $('detail-dialog').events.close(),
  };
}
test('UTC timestamps without suffix, with Z and with offset render the same Vietnam time', async () => {
  const h = harness(async () => result([row(1), row(2, '2026-10-19T08:00:00Z'), row(3, '2026-10-19T15:00:00+07:00')]));
  await h.ui.load();
  assert.equal((h.$('order-lookup-rows').innerHTML.match(/15:00:00/g) || []).length, 3);
  assert.ok(h.dates.every(value => /Z$|\+07:00$/.test(value)));
  assert.match(h.$('order-lookup-rows').innerHTML, /&lt;Buyer&gt;/);
  assert.match(h.$('order-lookup-rows').innerHTML, /Đã thanh toán/);
  assert.match(h.$('order-lookup-rows').innerHTML, /Có lần thử thất bại trước đó/);
});
test('explicit code type is submitted; pagination and refresh keep applied filters, not unsent edits', async () => {
  const h = harness();
  Object.assign(h.formValues, { searchType: 'Payment', search: '100' });
  await h.submit();
  let query = new URLSearchParams(h.calls.at(-1).split('?')[1]);
  assert.equal(query.get('searchType'), 'Payment'); assert.equal(query.get('search'), '100'); assert.equal(query.get('page'), '1');
  h.formValues.search = '999';
  await h.paginate(2);
  query = new URLSearchParams(h.calls.at(-1).split('?')[1]);
  assert.equal(query.get('search'), '100'); assert.equal(query.get('page'), '2');
  await h.$('refresh-order-lookup').onclick();
  assert.equal(new URLSearchParams(h.calls.at(-1).split('?')[1]).get('search'), '100');
  await h.submit();
  assert.equal(new URLSearchParams(h.calls.at(-1).split('?')[1]).get('page'), '1');
});
test('only the newest search can update results or loading state', async () => {
  const pending = [];
  const h = harness(() => new Promise(resolve => pending.push(resolve)));
  const first = h.ui.load(1), second = h.ui.load(2);
  pending[1](result([row(222)], 2)); await second;
  pending[0](result([row(111)], 1)); await first;
  assert.match(h.$('order-lookup-rows').innerHTML, /#222/);
  assert.doesNotMatch(h.$('order-lookup-rows').innerHTML, /#111/);
  assert.equal(h.$('table-wrap').attrs['aria-busy'], 'false');
});
test('error has retry, empty results offer clear filters and do not reuse error text', async () => {
  let fail = true;
  const h = harness(async () => { if (fail) throw new Error('Mất kết nối'); return result([]); });
  await assert.rejects(h.ui.load(), /Mất kết nối/);
  assert.equal(h.$('lookup-empty-action').textContent, 'Thử lại');
  fail = false;
  await h.$('lookup-empty-action').onclick();
  assert.equal(h.$('lookup-empty-title').textContent, 'Không tìm thấy đơn phù hợp');
  assert.equal(h.$('lookup-empty-action').textContent, 'Xem tất cả đơn');
  assert.equal(h.$('order-lookup-pagination').hidden, true);
});
test('detail separates sections, labels simulated shipping refund, escapes text and normalizes every timestamp', async () => {
  const h = harness(); h.click(100); await tick();
  const html = h.html();
  for (const part of ['overview', 'payments', 'refunds', 'shipments']) assert.ok(html.includes(`data-lookup-panel="${part}"`));
  assert.match(html, /Giả lập nội bộ/);
  assert.match(html, /Chiều trả · Người mua → Người bán/);
  assert.match(html, /Return:Delivered/);
  assert.match(html, /&lt;script&gt;bad&lt;\/script&gt;/);
  assert.match(html, /\$100.00/); assert.match(html, /\$20.00/); assert.match(html, /\$80.00/);
  assert.ok(h.dates.every(value => /Z$|\+07:00$/.test(value)));
});
test('late detail responses cannot replace a newer order or reopen a closed detail', async () => {
  const pending = [];
  const h = harness(() => new Promise(resolve => pending.push(resolve)));
  h.click(100); h.click(200);
  pending[1](detail(200)); await tick();
  pending[0](detail(100)); await tick();
  assert.match(h.html(), /data-admin-order-detail="200"/);
  h.click(300); h.close(); const before = h.html();
  pending[2](detail(300)); await tick();
  assert.equal(h.html(), before);
});
test('quick filters reset to page one; clearing restores defaults', async () => {
  const h = harness();
  await h.quick.find(x => x.dataset.lookupQuick === 'HasRefund').onclick();
  assert.equal(new URLSearchParams(h.calls.at(-1).split('?')[1]).get('quickFilter'), 'HasRefund');
  await h.$('clear-order-lookup-filters').onclick();
  const query = new URLSearchParams(h.calls.at(-1).split('?')[1]);
  assert.equal(query.has('quickFilter'), false); assert.equal(query.get('page'), '1');
  assert.equal(h.$('clear-order-lookup-filters').hidden, true);
});
