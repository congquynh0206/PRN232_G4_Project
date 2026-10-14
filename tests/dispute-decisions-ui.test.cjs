const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const quote = { outcome: 'PartialRefund', responsibility: 'Buyer', buyerRefund: 40, maximumRefund: 85,
  outboundShippingRetained: 5, returnShippingCost: 5, buyerShippingRecovery: 5, subsidyReversal: 5,
  sellerGrossRefund: 50, feeCredit: 6.14, sellerNetDebit: 38.86, shippingPayer: 'Buyer', pricingFingerprint: 'a'.repeat(64) };
function load(api = async path => path.endsWith('decision-preview') ? quote : { case: { status: 'Escalated', orderId: 4 },
  entries: [{ id: 5, kind: 'Evidence', actorRole: 'buyer', description: '<script>unsafe</script>' }] }) {
  const nodes = new Map();
  const node = id => {
    if (!nodes.has(id)) nodes.set(id, { value: '', open: false, setAttribute(key, value) { this[key] = value; },
      focus() { this.focused = true; }, showModal() { this.open = true; }, close() { this.open = false; } });
    return nodes.get(id);
  };
  let key = 0, evidence = [];
  const context = { G4: { $: node, api, esc: x => String(x ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]),
    money: x => '$' + Number(x).toFixed(2), message() {} }, document: { querySelectorAll: () => evidence }, crypto: { randomUUID: () => 'key-' + ++key } };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/dispute-decisions.js', 'utf8'), context);
  context.G4.DisputeDecisions.init();
  return { ui: context.G4.DisputeDecisions, node, evidence(values) { evidence = values.map(value => ({ value })); } };
}
test('decision quote distinguishes cash, prepaid postage, coupon reversal and seller net debit', () => {
  const { ui } = load(); const html = ui.quoteHtml(quote);
  assert.match(html, /\$40.00/); assert.match(html, /\$38.86/); assert.match(html, /không thu lần nữa/);
  assert.match(html, /Coupon không cộng/); assert.match(html, /Người mua/);
});
test('opening escapes case evidence and confirmation requires a server preview', async () => {
  const { ui, node } = load(); await ui.open(1);
  assert.match(node('decision-evidence').innerHTML, /&lt;script&gt;/);
  assert.doesNotMatch(node('decision-evidence').innerHTML, /<script>/);
  assert.equal(node('decision-confirm').disabled, true);
});
test('seller win implies buyer responsibility; changing inputs discards old preview', async () => {
  const { ui, node } = load(); await ui.open(1); await node('decision-preview').onclick();
  assert.equal(node('decision-confirm').disabled, false);
  node('decision-outcome').value = 'SellerWins'; node('decision-outcome').onchange();
  assert.equal(node('decision-responsibility').value, 'Buyer'); assert.equal(node('decision-responsibility').disabled, true);
  assert.equal(node('decision-confirm').disabled, true);
  node('decision-outcome').value = 'PartialRefund'; node('decision-outcome').onchange();
  assert.equal(node('decision-responsibility').disabled, false); assert.equal(node('decision-amount-row').hidden, false);
  assert.equal(node('decision-amount').disabled, false);
  node('decision-outcome').value = 'FullRefund'; node('decision-outcome').onchange();
  assert.equal(node('decision-amount').disabled, true);
});
test('late preview cannot re-enable confirmation after amount changes', async () => {
  let resolve;
  const { ui, node } = load(path => path.endsWith('decision-preview') ? new Promise(r => { resolve = r; }) : Promise.resolve({ case: { status: 'Escalated', orderId: 4 }, entries: [] }));
  await ui.open(1); const pending = node('decision-preview').onclick();
  node('decision-amount').value = '50'; node('decision-amount').oninput(); resolve(quote); await pending;
  assert.equal(node('decision-confirm').disabled, true); assert.doesNotMatch(node('decision-quote').innerHTML, /\$40.00/);
});
test('saving requires evidence and retries uncertain response using same key and cash input only', async () => {
  const posted = [];
  const { ui, node, evidence } = load(async (path, method, data) => {
    if (path.endsWith('resolution')) { posted.push(data); throw new Error('Uncertain response'); }
    return path.endsWith('decision-preview') ? quote : { case: { status: 'Escalated', orderId: 4 }, entries: [] };
  });
  await ui.open(1); node('decision-outcome').value = 'PartialRefund'; node('decision-outcome').onchange();
  node('decision-amount').value = '40'; await node('decision-preview').onclick(); node('decision-reason').value = 'Evidence reviewed';
  await node('decision-form').onsubmit({ preventDefault() {} }); assert.equal(posted.length, 0); assert.equal(node('decision-error').focused, true);
  evidence([5]); const pending = node('decision-form').onsubmit({ preventDefault() {} });
  assert.equal(node('decision-fields').disabled, true); await pending;
  await node('decision-form').onsubmit({ preventDefault() {} });
  assert.equal(posted[0].key, posted[1].key); assert.equal(posted[0].refundAmount, 40);
  assert.deepEqual(Object.keys(posted[0]).sort(), ['evidenceEntryIds','key','outcome','pricingFingerprint','reason','refundAmount','responsibility']);
});
