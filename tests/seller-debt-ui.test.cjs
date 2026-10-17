const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
function load(api) {
  const nodes = new Map(), saved = new Map();
  const node = (id) => {
    if (!nodes.has(id)) nodes.set(id, { value: '', textContent: '', innerHTML: '', checked: false,
      disabled: false, setAttribute(k, v) { this[k] = v; }, focus() { this.focused = true; }, querySelectorAll() { return []; } });
    return nodes.get(id);
  };
  const context = { G4: { $: node, api, esc: (s) => String(s ?? '').replace(/[&<>"']/g,
    (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]),
    money: (s) => '$' + Number(s).toFixed(2), date: (s) => s || 'Chưa có', pager() {}, safe: (f) => f() },
    crypto: { randomUUID: () => 'same-key' }, URLSearchParams, sessionStorage: { getItem: (k) => saved.get(k),
      setItem: (k, v) => saved.set(k, v), removeItem: (k) => saved.delete(k) } };
  vm.createContext(context); vm.runInContext(fs.readFileSync('frontend/wwwroot/js/seller-debt.js', 'utf8'), context);
  context.G4.SellerDebt.init('seller'); node('seller-debt-history-kind').value = 'repayments';
  return { ui: context.G4.SellerDebt, node, saved };
}
const debt = { sellerId: 2, balance: 40, cycleNumber: 1, startedAt: '2026-10-14T10:00:00', dueAt: '2026-10-14T10:01:00',
  ageSeconds: 60, overdue: true, suspended: true, accountStatus: 'Active' };
test('seller sees earliest permanent restriction date; clearing debt after permanent block keeps the restriction explicit', () => {
  const { ui } = load();
  assert.match(ui.summaryHtml({ ...debt, earliestWriteOffAt: '2026-10-14T10:05:00' }), /sớm nhất từ 2026-10-14T10:05:00Z/);
  const blocked = ui.summaryHtml({ ...debt, balance: 0, permanentSalesBlockedAt: '2026-10-14T10:05:00' });
  assert.match(blocked, /Khóa bán vĩnh viễn/); assert.match(blocked, /Trả hết nợ không tự gỡ/);
  assert.doesNotMatch(blocked, /Không còn nợ<\/strong>/);
});
test('overdue state explains permitted existing work; history escapes bank and audit text', () => {
  const { ui } = load();
  assert.match(ui.summaryHtml(debt), /đình chỉ bán hàng/); assert.match(ui.summaryHtml(debt), /return\/dispute/);
  assert.match(ui.summaryHtml(debt), /10:01:00Z/);
  assert.match(ui.historyHtml([{ id: 1, status: 'Rejected', bankReferenceId: '<script>', amount: 5 }], 'repayments'), /Ngân hàng từ chối/);
  assert.doesNotMatch(ui.historyHtml([{ id: 1, bankReferenceId: '<script>' }], 'repayments'), /<script>/);
  assert.match(ui.historyHtml([{ eventType: 'DebtRestored', description: '<img>' }], 'events'), /&lt;img&gt;/);
});
test('uncertain repayment freezes amount and retries original key and snapshot after refreshed debt becomes zero', async () => {
  const posted = []; let current = debt;
  const { ui, node, saved } = load(async (path, method, payload) => {
    if (method === 'POST') { posted.push(payload); throw new Error('Connection lost'); }
    return path === 'seller/debt' ? current : { page: 1, items: [] };
  });
  await ui.loadSeller(); node('debt-repayment-amount').value = '10';
  await node('debt-repayment-form').onsubmit({ preventDefault() {} });
  assert.equal(node('debt-repayment-amount').disabled, true);
  assert.equal(node('debt-repayment-submit').disabled, false); assert.equal(saved.size, 1);
  current = { ...debt, balance: 0 }; await ui.loadSeller();
  await node('debt-repayment-form').onsubmit({ preventDefault() {} });
  assert.equal(posted[0].key, posted[1].key); assert.equal(posted[1].expectedDebt, 40);
  assert.equal(posted[1].expectedCycleNumber, 1); assert.equal(posted[1].amount, 10);
});
test('stale debt response discards failed request and updates balance for explicit resubmission', async () => {
  let posts = 0; const data = [];
  const { ui, node, saved } = load(async (path, method, payload) => {
    if (method === 'POST') { posts++; data.push(payload); const e = new Error('Khoản nợ đã thay đổi'); e.status = 409; throw e; }
    return path === 'seller/debt' ? { ...debt, balance: posts ? 20 : 40 } : { page: 1, items: [] };
  });
  await ui.loadSeller(); node('debt-repayment-amount').value = '10';
  await node('debt-repayment-form').onsubmit({ preventDefault() {} });
  assert.equal(saved.size, 0); assert.equal(node('debt-repayment-amount').disabled, false);
  assert.equal(node('debt-repayment-amount').max, '20'); assert.equal(node('debt-repayment-error').focused, true);
  await node('debt-repayment-form').onsubmit({ preventDefault() {} });
  assert.equal(data[1].expectedDebt, 20);
});
test('all-debt button fills viewed amount, invalid excess is rejected before post, successful receipt refreshes', async () => {
  const posted = [];
  const { ui, node, saved } = load(async (path, method, payload) => {
    if (method === 'POST') { posted.push(payload); return { id: 8, status: 'Succeeded', bankReferenceId: 'BANK-8', remainingDebt: 0 }; }
    return path === 'seller/debt' ? debt : { page: 1, items: [] };
  });
  await ui.loadSeller(); node('debt-repayment-all').onclick(); assert.equal(node('debt-repayment-amount').value, 40);
  node('debt-repayment-amount').value = '41'; await node('debt-repayment-form').onsubmit({ preventDefault() {} });
  assert.equal(posted.length, 0);
  node('debt-repayment-amount').value = '40'; await node('debt-repayment-form').onsubmit({ preventDefault() {} });
  assert.equal(posted.length, 1); assert.equal(saved.size, 0);
  assert.match(node('debt-repayment-result').textContent, /Biên nhận #8/);
});
