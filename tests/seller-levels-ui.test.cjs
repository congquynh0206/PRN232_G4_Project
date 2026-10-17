const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
function load(name, api) {
  const nodes = new Map();
  const $ = (id) => {
    if (!nodes.has(id)) nodes.set(id, { value: '', textContent: '', innerHTML: '', disabled: false, checked: false,
      setAttribute(k, v) { this[k] = v; }, focus() { this.focused = true; }, addEventListener() {},
      showModal() { this.open = true; }, close() { this.open = false; } });
    return nodes.get(id);
  };
  const context = { G4: { $, api, esc: (s) => String(s ?? '').replace(/[<>&"']/g, (c) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&#39;' })[c]),
    date: (s) => s, money: (n) => '$' + n, message() {}, safe: async (f) => f(), pager() {} },
    crypto: { randomUUID: () => 'feedback-retry-key' }, URLSearchParams, Date };
  vm.createContext(context); vm.runInContext(fs.readFileSync(`frontend/wwwroot/js/${name}.js`, 'utf8'), context);
  return { $, ui: context.G4, context };
}
test('level criteria distinguish missing evidence from a failed criterion and escape names/reasons', () => {
  const { ui } = load('seller-levels');
  const html = ui.SellerLevels.decisionHtml({ previousLevel: 2, resultLevel: 2, reason: '<script>',
    metrics: { windowStart: '2026-10-01', windowEnd: '2026-10-15', paidOrders: 20, feedbackCount: 0, deliveryCount: 1 },
    tiers: [{ level: 3, criteria: [{ label: '<img>', actual: null, required: 97, passed: null }, { label: 'Nợ', actual: 1, required: 0, maximum: true, passed: false }] }] });
  assert.match(html, /&lt;script&gt;/); assert.match(html, /&lt;img&gt;/); assert.doesNotMatch(html, /<script>|<img>/);
  assert.match(html, /Chưa có dữ liệu/); assert.match(html, /Chưa đủ dữ liệu/); assert.match(html, /Chưa đạt/);
});
test('simulation posts only to side effect free development endpoint, preserving missing feedback as null', async () => {
  const calls = [];
  const { ui, $ } = load('seller-levels', async (path, method, body) => { calls.push({ path, method, body }); throw new Error('QA unavailable'); });
  ui.SellerLevels.init('seller'); ui.SellerLevels.scenario('missing');
  await ui.SellerLevels.simulate({ preventDefault() {} });
  assert.equal(calls.length, 1); assert.equal(calls[0].path, 'dev/seller-levels/simulate');
  assert.equal(calls[0].body.metrics.positiveFeedbackRate, null); assert.equal(calls[0].body.metrics.onTimeDeliveryRate, null);
  assert.equal($('level-simulate').disabled, false); assert.equal($('level-simulation-error').focused, true);
});
test('feedback HTML escapes buyer text and offers action only when server allows it', () => {
  const { ui } = load('seller-feedback');
  const html = ui.SellerFeedback.html({ id: 4, sellerFeedback: { rating: 5, comment: '<script>alert(1)</script>' } });
  assert.match(html, /&lt;script&gt;/); assert.doesNotMatch(html, /<script>/);
  assert.equal(ui.SellerFeedback.html({ id: 4, canReviewSeller: false }), '');
  assert.match(ui.SellerFeedback.html({ id: 4, canReviewSeller: true }), /review-seller/);
});
test('unknown feedback result freezes fields and retries exact original rating/comment/key', async () => {
  const calls = [];
  const { ui, $ } = load('seller-feedback', async (path, method, body) => { calls.push(JSON.parse(JSON.stringify(body))); throw new Error('Lost connection'); });
  ui.SellerFeedback.init(); ui.SellerFeedback.open(5);
  $('seller-feedback-rating').value = '4'; $('seller-feedback-comment').value = ' Good ';
  await ui.SellerFeedback.submit({ preventDefault() {} });
  assert.equal($('seller-feedback-fields').disabled, true); assert.equal($('seller-feedback-submit').disabled, false);
  $('seller-feedback-rating').value = '1'; $('seller-feedback-comment').value = 'changed';
  await ui.SellerFeedback.submit({ preventDefault() {} });
  assert.deepEqual(calls[0], calls[1]); assert.equal(calls[1].rating, 4); assert.equal(calls[1].comment, 'Good');
  assert.match($('seller-feedback-submit').textContent, /cùng đánh giá/);
});
test('feedback validates rating before sending and successful save closes dialog then refreshes order', async () => {
  let calls = 0, refreshed = 0;
  const { ui, $ } = load('seller-feedback', async () => { calls++; return {}; });
  ui.SellerFeedback.init(); ui.SellerFeedback.open(5, async () => refreshed++);
  await ui.SellerFeedback.submit({ preventDefault() {} }); assert.equal(calls, 0);
  $('seller-feedback-rating').value = '5'; await ui.SellerFeedback.submit({ preventDefault() {} });
  assert.equal(calls, 1); assert.equal(refreshed, 1); assert.equal($('seller-feedback-dialog').open, false);
});
test('definitive feedback rejection unlocks fields and never claims success', async () => {
  const { ui, $ } = load('seller-feedback', async () => { throw Object.assign(new Error('Đơn chưa hoàn tất'), { status: 409 }); });
  ui.SellerFeedback.init(); ui.SellerFeedback.open(5); $('seller-feedback-rating').value = '5';
  await ui.SellerFeedback.submit({ preventDefault() {} });
  assert.equal($('seller-feedback-fields').disabled, false); assert.equal($('seller-feedback-dialog').open, true);
  assert.equal($('seller-feedback-error').textContent, 'Đơn chưa hoàn tất');
});
