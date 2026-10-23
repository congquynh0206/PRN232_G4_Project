const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function helpers(overrides = {}) {
  const elements = new Map(),
    listeners = [],
    calls = [],
    messages = [];
  const element = (id) => {
    if (!elements.has(id)) elements.set(id, { value: '', disabled: false });
    return elements.get(id);
  };
  const G4 = {
    $: element,
    esc: (value) =>
      String(value ?? '').replace(
        /[<>&"]/g,
        (x) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;' })[x],
      ),
    money: (value) => '$' + Number(value || 0).toFixed(2),
    date: (value) => value,
    safe: (fn) => fn(),
    message: (...args) => messages.push(args),
    modal: async () => ({ reason: 'Rejected reason' }),
    api: async (route, verb, body) => {
      calls.push({ route, body });
      return { status: 'Approved' };
    },
  };
  Object.assign(G4, overrides);
  const context = {
    G4,
    crypto: { randomUUID: () => 'key' },
    document: {
      body: { addEventListener: (type, fn) => listeners.push(fn) },
      querySelectorAll: () => [],
    },
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/address-changes.js', 'utf8'), context);
  return { ...G4.AddressChanges, G4, listeners, calls, messages };
}
const row = {
  id: 2,
  orderId: 1,
  status: 'Pending',
  oldAddress: 'Old address',
  newAddress: '<script>New</script>',
  reason: 'Move',
  oldShippingCost: 2.1,
  newShippingCost: 5.2,
  sellerCap: 4,
  buyerRefund: 0,
  createdAt: '2026-10-13T09:00:00',
};

test('buyer sees request button only when server says change is allowed', () => {
  const h = helpers();
  assert.match(
    h.summaryHtml({ id: 1, canRequestAddressChange: true }, 'buyer'),
    /Yêu cầu đổi địa chỉ/,
  );
  assert.equal(h.summaryHtml({ id: 1, canRequestAddressChange: false }, 'buyer'), '');
  assert.doesNotMatch(
    h.summaryHtml({ id: 1, canRequestAddressChange: true }, 'seller'),
    /data-address-change="request"/,
  );
});
test('history escapes addresses and reasons; seller sees pending decision and its cost', () => {
  const html = helpers().summaryHtml({ id: 1, addressChanges: [row] }, 'seller');
  assert.match(html, /&lt;script&gt;/);
  assert.doesNotMatch(html, /<script>/);
  assert.match(html, /\$3.10/);
  assert.match(html, /data-address-change="approve"/);
  assert.match(html, /data-address-change="reject"/);
  assert.doesNotMatch(
    helpers().summaryHtml({ id: 1, addressChanges: [{ ...row, status: 'Approved' }] }, 'seller'),
    /data-address-change="approve"/,
  );
});
test('refund presentation distinguishes proposed refund and completed simulated refund', () => {
  const h = helpers();
  assert.match(
    h.summaryHtml({ id: 1, addressChanges: [{ ...row, buyerRefund: 3.1 }] }, 'buyer'),
    /dự kiến khi đủ xác nhận/,
  );
  assert.match(
    h.summaryHtml(
      { id: 1, addressChanges: [{ ...row, buyerRefund: 3.1, status: 'Approved' }] },
      'buyer',
    ),
    /Đã hoàn.*giả lập/,
  );
});
test('legacy approved shipment retains acknowledgement without approving a pending request', () => {
  const h = helpers(),
    shipment = { id: 5, addressChangeRequestId: 2, addressChangeAcknowledgedAt: null };
  assert.equal(h.requiresAck(shipment), true);
  assert.match(
    h.shipperNotice(shipment, { addressChanges: [{ ...row, status: 'Approved' }] }),
    /data-ack-address="5"/,
  );
  assert.equal(h.requiresAck({ ...shipment, addressChangeAcknowledgedAt: '2026-10-13' }), false);
  assert.match(
    h.shipperNotice(
      { ...shipment, addressChangeAcknowledgedAt: '2026-10-13' },
      { addressChanges: [{ ...row, status: 'Approved' }] },
    ),
    /Đã xác nhận/,
  );
});

test('awaiting shipper shows accept/refuse before application; only outbound delivery start is blocked', () => {
  const h = helpers(),
    shipment = { id: 5, direction: 'Outbound' };
  const order = {
    addressChanges: [{ ...row, status: 'AwaitingShipper', expiresAt: '2026-10-20T10:00:00Z' }],
  };
  const html = h.shipperNotice(shipment, order);
  assert.match(html, /chưa có hiệu lực/);
  assert.match(html, /data-address-shipper="approve"/);
  assert.match(html, /data-address-shipper="reject"/);
  assert.match(html, /data-request="2"/);
  assert.doesNotMatch(html, /data-ack-address/);
  assert.equal(h.blocksDelivery(shipment, order), true);
  assert.equal(h.blocksDelivery({ ...shipment, direction: 'Return' }, order), false);
  const seller = h.summaryHtml({ id: 1, addressChanges: order.addressChanges }, 'seller');
  assert.match(seller, /Địa chỉ cũ còn hiệu lực/);
  assert.doesNotMatch(seller, /data-address-change="approve"/);
});

test('buyer confirms a server quote before submit; unknown response reuses key and confirmed quote', async () => {
  const calls = [],
    modals = [];
  const input = {
    fullName: 'Buyer',
    street: 'New',
    city: 'Hanoi',
    state: 'Hanoi',
    country: 'Vietnam',
    reason: 'Move',
  };
  let fail = true;
  const h = helpers({
    modal: async (options) => {
      modals.push(options);
      return options.fields ? input : {};
    },
    api: async (route, method, body) => {
      calls.push({ route, body });
      if (route === 'addresses') return [];
      if (route.endsWith('/quote'))
        return {
          token: 'server-quote',
          oldAddress: 'Old',
          newAddress: 'New',
          oldShippingCost: 5.2,
          newShippingCost: 2.1,
          sellerCap: 0,
          buyerRefund: 3.1,
          withinCap: true,
          shipperId: 3,
        };
      if (fail) {
        fail = false;
        throw new Error('network');
      }
      return { status: 'Pending' };
    },
  });
  h.init('buyer', async () => {});
  const event = {
    target: { closest: () => ({ dataset: { addressChange: 'request', addressOrder: '8' } }) },
  };
  await assert.rejects(h.listeners[0](event), /network/);
  await h.listeners[0](event);
  assert.equal(modals.length, 2);
  assert.match(modals[1].description, /Buyer không trả thêm/);
  assert.match(modals[1].description, /\$3.10/);
  const sends = calls.filter((x) => x.route === 'orders/8/address-changes');
  assert.equal(sends.length, 2);
  assert.deepEqual(sends[0].body, sends[1].body);
  assert.equal(sends[0].body.quoteToken, 'server-quote');
  assert.equal(calls.filter((x) => x.route.endsWith('/quote')).length, 1);
});

test('shipper decision includes request identity and a reason when refusing', async () => {
  const h = helpers();
  await h.decideShipper({ dataset: { addressShipper: 'reject', shipment: '5', request: '12' } });
  assert.equal(h.calls[0].route, 'shipper/shipments/5/address-change/decision');
  assert.equal(h.calls[0].body.requestId, 12);
  assert.equal(h.calls[0].body.approve, false);
  assert.equal(h.calls[0].body.reason, 'Rejected reason');
});
test('seller decision uses request ID and refreshes the correct order', async () => {
  const h = helpers(),
    refreshed = [];
  h.init('seller', (id) => refreshed.push(id));
  const button = { dataset: { addressOrder: '8', addressRequest: '12', addressChange: 'approve' } };
  await h.listeners[0]({ target: { closest: () => button } });
  assert.equal(h.calls[0].route, 'seller/address-changes/12/decision');
  assert.equal(h.calls[0].body.approve, true);
  assert.deepEqual(refreshed, [8]);
});
