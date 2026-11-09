const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const frontend = path.resolve(__dirname, '..');
const source = (file) => fs.readFileSync(path.join(frontend, file), 'utf8');

function harness() {
  const elements = new Map(), calls = [];
  const element = (id) => {
    if (!elements.has(id)) elements.set(id, {
      value: '', checked: false, disabled: false, hidden: false, textContent: '', innerHTML: '',
      classList: { toggle() {} }, setAttribute() {}, addEventListener() {}, focus() {},
      querySelectorAll: () => [], showModal() { this.open = true; }, close() { this.open = false; },
    });
    return elements.get(id);
  };
  let addresses = [{ id: 1, fullName: 'Buyer', street: '1 Main', city: 'Boston', state: 'MA', country: 'US', postalCode: null, isDefault: true }];
  const G4 = { $: element, esc: (v) => String(v ?? '').replace(/</g, '&lt;'),
    safe: (fn) => fn(), message() {}, modal: async () => true,
    api: async (route, method, body) => {
      calls.push({ route, method, body });
      if (route === 'addresses') return addresses;
      if (route === 'addresses/1/edit') {
        addresses = [{ ...addresses[0], ...body }];
        return { addresses };
      }
      throw Error(route);
    },
  };
  vm.runInNewContext(source('wwwroot/js/buyer-addresses.js'), { G4, document: { activeElement: null } });
  return { G4, element, calls };
}

test('address edit persists optional US ZIP and invalidates selected address key', async () => {
  const h = harness(), changed = [];
  const book = h.G4.BuyerAddresses.create({ onChange: (v) => changed.push(v) });
  await book.load();
  const oldKey = book.selectedKey();
  book.openManage();
  h.element('address-manage-list').onclick({ target: { closest: () => ({ dataset: { act: 'edit', id: '1' } }) } });
  for (const [field, value] of Object.entries({ fullName: 'Buyer', street: '1 Main', city: 'Boston', state: 'MA', country: 'US', postalCode: '02108' }))
    h.element('address-form-' + field).value = value;
  await h.element('address-form').onsubmit({ preventDefault() {} });
  assert.equal(h.calls.at(-1).route, 'addresses/1/edit');
  assert.equal(h.calls.at(-1).body.postalCode, '02108');
  assert.notEqual(book.selectedKey(), oldKey);
  assert.equal(changed.at(-1).address.postalCode, '02108');
  assert.match(h.element('address-list').innerHTML, /02108/);
});

test('US ZIP is optional in address book but supplied ZIP must be well formed', async () => {
  const h = harness();
  const book = h.G4.BuyerAddresses.create({ onChange() {} });
  await book.load();
  book.openManage();
  h.element('address-manage-list').onclick({ target: { closest: () => ({ dataset: { act: 'edit', id: '1' } }) } });
  for (const [field, value] of Object.entries({ fullName: 'Buyer', street: '1 Main', city: 'Boston', state: 'MA', country: 'US', postalCode: 'not-a-zip' }))
    h.element('address-form-' + field).value = value;
  await h.element('address-form').onsubmit({ preventDefault() {} });
  assert.match(h.element('address-error-postalCode').textContent, /ZIP/);
  assert.equal(h.calls.some((c) => c.route === 'addresses/1/edit'), false);
  h.element('address-form-postalCode').value = '';
  await h.element('address-form').onsubmit({ preventDefault() {} });
  assert.equal(h.calls.at(-1).body.postalCode, '');
});

test('Klarna checkout asks for real ZIP and blocks only order creation, not quote', () => {
  const cart = source('wwwroot/js/buyer-cart.js');
  const view = source('Views/Buyer/Index.cshtml');
  assert.match(view, /id="klarna-zip-note"/);
  assert.match(source('Views/Shared/_BuyerAddressDialog.cshtml'), /id="address-form-postalCode"/);
  assert.match(cart, /const canPay = \(\) => canQuote\(\) && !klarnaZipNeeded\(\)/);
  assert.match(cart, /Klarna US cần địa chỉ tại Hoa Kỳ với ZIP thật/);
  assert.match(cart, /if \(token !== quoteRequest \|\| !canQuote\(\)/);
});
