const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
function load(api = async () => ({})) {
  const nodes = new Map();
  const node = (id) => { if (!nodes.has(id)) nodes.set(id, { value: '', focus() { this.focused = true; } }); return nodes.get(id); };
  const context = { G4: { $: node, api, esc: (x) => String(x ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]),
    date: (x) => x, money: (x) => '$' + Number(x).toFixed(2), pager() {}, message() {}, safe: (f) => f() },
    crypto: { randomUUID: () => 'test-key' }, Date };
  vm.createContext(context);
  vm.runInContext(fs.readFileSync('frontend/wwwroot/js/platform-fees.js', 'utf8'), context);
  return { ui: context.G4.PlatformFees, node };
}
test('audit distinguishes scheduled/current/previous and escapes untrusted actor/reason', () => {
  const { ui } = load();
  const rows = [
    { id: 2, effectiveAt: '2099-01-01T00:00:00', feePercent: 13, fixedFee: .3, previousFeePercent: 12, previousFixedFee: .3, changedBy: '<img>', reason: '<script>' },
    { id: 1, effectiveAt: '2000-01-01T00:00:00Z', feePercent: 12, fixedFee: .3 },
    { id: 3, effectiveAt: '2001-01-01T00:00:00Z', feePercent: 11, fixedFee: .3 },
  ];
  const html = ui.historyHtml(rows, 1, Date.parse('2026-10-14T00:00:00Z'));
  assert.match(html, /Chờ hiệu lực/); assert.match(html, /Đang áp dụng/); assert.match(html, /Đã thay thế/);
  assert.match(html, /&lt;script&gt;/); assert.match(html, /&lt;img&gt;/);
  assert.match(html, /2099-01-01T00:00:00Z/); assert.doesNotMatch(html, /<script>|<img>/);
});
test('scheduled local time is converted to UTC; immediate choice sends null', () => {
  const { ui, node } = load();
  node('platform-fee-percent').value = '12'; node('platform-fee-fixed').value = '.30'; node('platform-fee-reason').value = ' Change ';
  node('platform-fee-timing').value = 'scheduled'; node('platform-fee-effective').value = '2099-01-01T12:00';
  assert.equal(ui.payload().effectiveAt, new Date('2099-01-01T12:00').toISOString());
  node('platform-fee-timing').value = 'now'; assert.equal(ui.payload().effectiveAt, null);
  assert.equal(ui.payload().reason, 'Change');
});
test('past schedule is rejected before making a request', () => {
  const { ui, node } = load(); node('platform-fee-timing').value = 'scheduled'; node('platform-fee-effective').value = '2000-01-01T00:00';
  assert.throws(() => ui.payload(), /tương lai/);
});
test('uncertain save freezes fields, keeps form and uses the same request key on retry', async () => {
  const posted = [];
  const { ui, node } = load(async (path, method, data) => { posted.push(data); throw new Error('Connection failed'); });
  ui.init();
  node('platform-fee-percent').value = '12'; node('platform-fee-fixed').value = '.30'; node('platform-fee-reason').value = 'Change';
  node('platform-fee-timing').value = 'now';
  const submit = node('platform-fee-form').onsubmit({ preventDefault() {} });
  assert.equal(node('platform-fee-fields').disabled, true);
  await submit;
  assert.equal(node('platform-fee-fields').disabled, false); assert.equal(node('platform-fee-error').focused, true);
  assert.equal(node('platform-fee-reason').value, 'Change');
  await node('platform-fee-form').onsubmit({ preventDefault() {} });
  assert.equal(posted[0].requestKey, posted[1].requestKey);
});
test('history pagination preserves entered data and the reviewed configuration version', async () => {
  let latest = 2;
  const { ui, node } = load(async () => ({ page: 1, totalCount: 1, latestPolicyId: latest,
    current: { id: 1, feePercent: 12, fixedFee: .3, effectiveAt: '2000-01-01T00:00:00' }, items: [] }));
  await ui.load(); node('platform-fee-reason').value = 'Unsaved'; latest = 3;
  await ui.load(2); assert.equal(node('platform-fee-reason').value, 'Unsaved'); assert.equal(ui.payload().expectedLatestPolicyId, 2);
});
