const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function harness() {
  let wall = Date.parse('2026-10-23T00:00:00Z'),
    elapsed = 0;
  const nodes = [];
  class Clock extends Date {
    static now() {
      return wall;
    }
  }
  const esc = (value) =>
    String(value ?? '').replace(
      /[&<>"']/g,
      (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
    );
  const context = {
    G4: { esc },
    Date: Clock,
    performance: { now: () => elapsed },
    document: { hidden: false, querySelectorAll: () => nodes, addEventListener() {} },
    setInterval() {},
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/countdowns.js', 'utf8'), context);
  return {
    timer: context.G4.Countdown,
    context,
    nodes,
    advance: (seconds) => {
      elapsed += seconds * 1000;
    },
    skew: (seconds) => {
      wall += seconds * 1000;
    },
  };
}

test('countdown uses UTC deadlines, server clock and elapsed time across clock changes', () => {
  const h = harness();
  h.skew(3600);
  h.timer.sync('2026-10-23T00:00:00Z');
  assert.match(h.timer.html('2026-10-23T00:05:00'), /05:00/);
  assert.match(h.timer.html('2026-10-23T07:05:00+07:00'), /05:00/);
  h.advance(17);
  h.skew(-7200);
  assert.match(h.timer.html('2026-10-23T00:05:00Z'), /04:43/);
  assert.equal(h.timer.format(3661), '01:01:01');
  assert.equal(h.timer.html(null), '');
  assert.equal(h.timer.html('invalid'), '');
});

test('expired timer requests a refresh once, stays pending and never mutates money', async () => {
  const h = harness();
  let refreshes = 0;
  const value = { textContent: '' },
    classes = new Set();
  const node = {
    dataset: {
      countdown: '2026-10-23T00:00:02Z',
      countdownKind: 'funds',
      countdownScope: '101',
      countdownExpired: 'Đã đủ thời gian giữ — chờ server',
    },
    querySelector: () => value,
    closest: () => null,
    classList: { toggle: (name, on) => (on ? classes.add(name) : classes.delete(name)) },
  };
  h.nodes.push(node);
  h.timer.subscribe('funds', () => refreshes++);
  h.timer.update();
  assert.equal(value.textContent, '00:02');
  assert.ok(classes.has('is-urgent'));
  h.advance(3);
  h.timer.update();
  h.timer.update();
  await Promise.resolve();
  assert.equal(value.textContent, 'Đã đủ thời gian giữ — chờ server');
  assert.ok(classes.has('is-expired'));
  assert.equal(refreshes, 1);
  h.nodes[0] = { ...node };
  h.timer.update();
  await Promise.resolve();
  assert.equal(refreshes, 1, 'rerendered expired deadline does not cause a request loop');
});

test('each workflow labels its actual consequence and never fabricates a deadline', () => {
  const { timer } = harness();
  const due = '2026-10-23T00:00:45Z';
  assert.equal(timer.payment({ status: 'Paid', paymentExpiresAt: due }), '');
  assert.match(
    timer.payment({ id: 1, status: 'AwaitingPayment', paymentExpiresAt: due }),
    /Thanh toán trong/,
  );
  assert.equal(timer.receipt({ status: 'Requested', confirmationDueAt: due }), '');
  assert.doesNotMatch(
    timer.receipt({ status: 'ReturnShipping', confirmationDueAt: due }, true),
    /data-countdown=/,
  );
  assert.match(
    timer.dispute({ id: 1, isOpen: true, status: 'AwaitingSeller', responseDueAt: due }),
    /chờ chuyển admin/,
  );
  assert.match(
    timer.dispute({ id: 1, isOpen: true, status: 'AwaitingBuyer', responseDueAt: due }),
    /đóng hồ sơ do buyer/,
  );
  assert.equal(timer.dispute({ isOpen: false, responseDueAt: due }), '');
  assert.equal(timer.address({ status: 'Approved', expiresAt: due }), '');
  assert.equal(timer.debt({ balance: 0, dueAt: due }), '');
  assert.match(timer.html(due, { label: '<img src=x>', expired: '"x"' }), /&lt;img/);
  assert.match(timer.html(due), /aria-live="off"/);
});

test('seller funds show separate remaining buckets, blocked holds and unshipped orders', () => {
  const h = harness();
  Object.assign(h.context.G4, {
    $() {},
    api() {},
    safe() {},
    pager() {},
    money: (value) => '$' + Number(value).toFixed(2),
    date: (value) => value,
  });
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/seller-funds.js', 'utf8'), h.context);
  const row = h.context.G4.SellerFunds.row;
  const item = {
    orderId: 101,
    processing: 5,
    onHold: 10,
    sellerLevel: 1,
    holdPolicyVersion: 1,
    holdSeconds: 30,
    releaseAt: '2026-10-23T00:00:30Z',
    blockReason: 'Dispute',
    disputeId: 3,
  };
  const html = row(item);
  assert.match(html, /\$5.00/);
  assert.match(html, /\$10.00/);
  assert.match(html, /00:30/);
  assert.match(html, /đang bị giữ do tranh chấp/);
  assert.match(html, /đang chờ xử lý trả hàng\/tranh chấp/);
  assert.doesNotMatch(row({ ...item, releaseAt: null }), /data-countdown=/);
  assert.match(row({ ...item, releaseAt: null }), /Chưa bắt đầu đếm/);
  assert.doesNotMatch(row({ ...item, processing: 0 }), /data-countdown=/);
});
