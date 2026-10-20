const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const period = (period, net = 0, sources = null) => ({ period, paidOrders: net ? 1 : 0, buyerPaid: net ? 100 : 0,
  buyerRefunded: 0, platformFee: net ? 12.30 : 0, net, platformSubsidy: 0, subsidyReversal: 0,
  shippingAdjustments: 0, returnShippingCost: 0, returnShippingRecovery: 0, sources });
const result = (items = [period('2026-10-19', 87.70)]) => ({ from: '2026-10-19', to: '2026-10-20', timeZone: 'Asia/Ho_Chi_Minh',
  group: 'day', totals: period('2026-10-19', 87.70), chart: [period('2026-10-19', 87.70), period('2026-10-20')],
  page: 1, pageSize: 10, totalCount: 2, items, unreconciledPayments: 0, unreconciledRefunds: 0 });
function harness(api) {
  const nodes = new Map(), calls = [], checks = ['buyerPaid', 'buyerRefunded', 'net'].map(k => ({ checked: true, dataset: { reportSeries: k } }));
  function $(id) {
    if (!nodes.has(id)) nodes.set(id, { value: '', innerHTML: '', textContent: '', attrs: {},
      setAttribute(k, v) { this.attrs[k] = v; } });
    return nodes.get(id);
  }
  $('report-preset').value = 'month'; $('report-group').value = 'day';
  const G4 = { $, api: async path => { calls.push(path); return api(path); },
    money: n => Number(n) < 0 ? '-$' + Math.abs(Number(n)).toFixed(2) : '$' + Number(n).toFixed(2), esc: s => String(s ?? '').replace(/[&<>"']/g,
      c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]), pager() {} };
  const document = { querySelectorAll: s => s === '[data-report-series]:checked' ? checks.filter(x => x.checked) : checks };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/seller-report.js', 'utf8'),
    { G4, document, Intl, Date, URLSearchParams });
  G4.SellerReport.init();
  return { $, calls, checks, ui: G4.SellerReport };
}
test('filter uses local zone and same source for cards/chart/table; source text escaped', async () => {
  const h = harness(async () => result([period('2026-10-19', 87.70, [{ transactionId: 1, orderId: 42,
    refundId: null, type: '<bad>', amount: 100 }])]));
  h.$('report-from').value = '2026-10-19'; h.$('report-to').value = '2026-10-20';
  await h.ui.load();
  const query = new URLSearchParams(h.calls[0].split('?')[1]);
  assert.equal(query.get('from'), '2026-10-19'); assert.ok(query.get('timeZone'));
  assert.match(h.$('report-cards').innerHTML, /\$87.70/);
  assert.match(h.$('report-chart').innerHTML, /polyline/);
  assert.match(h.$('report-rows').innerHTML, /&lt;bad&gt;/);
  assert.match(h.$('report-rows').innerHTML, /Seller\?orderId=42/);
  h.checks[0].checked = false; h.checks[0].onchange();
  assert.doesNotMatch(h.$('report-chart').innerHTML, /Buyer đã trả: /);
});
test('invalid dates and late response cannot show old report', async () => {
  const pending = [];
  const h = harness(() => new Promise(resolve => pending.push(resolve)));
  h.$('report-from').value = '2026-10-21'; h.$('report-to').value = '2026-10-20';
  await h.ui.load(); assert.equal(h.calls.length, 0);
  h.$('report-from').value = '2026-10-19';
  const first = h.ui.load(); const second = h.ui.load();
  pending[1](result([period('2026-10-20', -17.54)])); await second;
  pending[0](result([period('2026-10-19', 87.70)])); await first;
  assert.match(h.$('report-rows').innerHTML, /-\$17.54/);
  assert.doesNotMatch(h.$('report-rows').innerHTML, /\$87.70/);
  assert.equal(h.$('report-status').attrs['aria-busy'], 'false');
});
test('failed fetch clears stale data and empty report shows zeros without demo values', async () => {
  let failed = false;
  const h = harness(async () => { if (failed) throw new Error('Mất kết nối');
    const empty = result([period('2026-10-20')]); empty.totals = period('2026-10-20'); empty.chart = [period('2026-10-20')]; return empty; });
  h.$('report-from').value = '2026-10-20'; h.$('report-to').value = '2026-10-20';
  await h.ui.load(); assert.match(h.$('report-status').textContent, /Không có phát sinh/);
  failed = true; await h.ui.load();
  assert.equal(h.$('report-cards').innerHTML, ''); assert.match(h.$('report-status').textContent, /Làm mới/);
});
