const test = require('node:test'), assert = require('node:assert/strict'), fs = require('node:fs'), vm = require('node:vm');
function harness(write) {
  const elements = new Map(), locks = [], calls = [], changes = [];
  function $(id) {
    if (!elements.has(id)) elements.set(id, { value: '', hidden: false, disabled: false, open: false, innerHTML: '', textContent: '', events: {}, setAttribute() {}, focus() {}, querySelectorAll() { return []; }, addEventListener(k, fn) { this.events[k] = fn; }, showModal() { this.open = true; }, close() { this.open = false; } });
    return elements.get(id);
  }
  const G4 = { $, esc: value => String(value ?? ''), message() {}, safe: fn => fn(), modal: async () => true,
    api: async (route, method, payload) => { if (route === 'addresses') return []; calls.push({ route, payload }); return write(route, payload); } };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/buyer-addresses.js', 'utf8'), { G4, document: { activeElement: null } });
  const book = G4.BuyerAddresses.create({ lock: value => locks.push(value), onChange: value => changes.push(value) });
  const fill = () => { for (const key of ['fullName', 'street', 'city', 'state', 'country']) $('address-form-' + key).value = 'Example'; };
  return { $, book, calls, locks, changes, fill };
}
test('pending address save locks save/cancel/close and Escape, prevents duplicate submit, then restores controls', async () => {
  let resolve;
  const h = harness(() => new Promise(done => { resolve = done; }));
  await h.book.load(); h.book.openManage(true); h.fill();
  const pending = h.$('address-form').onsubmit({ preventDefault() {} });
  for (const id of ['address-form-save', 'address-form-cancel', 'address-dialog-close']) assert.equal(h.$(id).disabled, true);
  let prevented = false;
  h.$('address-dialog').events.cancel({ preventDefault() { prevented = true; } }); assert.equal(prevented, true);
  h.$('address-form-cancel').onclick(); h.$('address-dialog-close').onclick();
  assert.equal(h.$('address-form').hidden, false); assert.equal(h.$('address-dialog').open, true);
  await h.$('address-form').onsubmit({ preventDefault() {} }); assert.equal(h.calls.length, 1);
  resolve({ addresses: [{ id: 1, fullName: 'Example', street: 'Example', city: 'Example', state: 'Example', country: 'Example', isDefault: true }] });
  await pending;
  assert.deepEqual(h.locks, [true, false]); assert.equal(h.$('address-form-save').disabled, false); assert.equal(h.$('address-form-cancel').disabled, false);
  assert.equal(h.book.selectedId(), 1); assert.equal(h.changes.length, 1);
});
test('failed save keeps form and values, unlocks retry, and disabled checkout prevents submission', async () => {
  const h = harness(async () => { throw new Error('Server unavailable'); });
  await h.book.load(); h.book.openManage(true); h.fill();
  await h.$('address-form').onsubmit({ preventDefault() {} });
  assert.equal(h.$('address-form').hidden, false); assert.equal(h.$('address-form-fullName').value, 'Example'); assert.equal(h.$('address-form-save').disabled, false);
  h.book.setDisabled(true);
  await h.$('address-form').onsubmit({ preventDefault() {} });
  assert.equal(h.calls.length, 1); assert.equal(h.$('address-form-save').disabled, true);
});
