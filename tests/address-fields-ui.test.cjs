const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function load(api = async () => ({ countries: [] })) {
  const G4 = { $: () => null, esc: (value) => String(value ?? ''), api };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/address-fields.js', 'utf8'), {
    G4,
    document: {},
  });
  return G4.AddressFields;
}

test('stored E.164 numbers are shown in national form for editing', () => {
  const f = load();
  assert.equal(f.national('+84912345678', 'Vietnam'), '0912345678');
  assert.equal(f.national('+12125550123', 'United States'), '2125550123');
  assert.equal(f.national('0900000000', 'Vietnam'), '0900000000');
  assert.equal(f.national('', 'Vietnam'), '');
});

test('read-only views group the digits by country', () => {
  const f = load();
  assert.equal(f.formatPhone('+84912345678'), '+84 912 345 678');
  assert.equal(f.formatPhone('+842438251234'), '+84 243 825 1234');
  assert.equal(f.formatPhone('+12125550123'), '+1 (212) 555-0123');
  assert.equal(f.formatPhone('0900000000'), '0900000000');
});

test('region list is requested once and retried after a failure', async () => {
  let calls = 0;
  const f = load(async () => {
    calls++;
    if (calls === 1) throw new Error('offline');
    return { countries: [] };
  });
  await assert.rejects(f.regions(), /offline/);
  await f.regions();
  await f.regions();
  assert.equal(calls, 2);
});
