const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function common(fetch) {
  let now = Date.parse('2026-10-28T03:00:00Z');
  class Clock extends Date {
    static now() {
      return now;
    }
  }
  const elements = new Map();
  const context = {
    window: {},
    Date: Clock,
    URL,
    fetch,
    setTimeout,
    clearTimeout,
    document: {
      getElementById(id) {
        if (!elements.has(id)) elements.set(id, { textContent: '', innerHTML: '' });
        return elements.get(id);
      },
    },
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/common.js', 'utf8'), context);
  return {
    api: context.window.G4.api,
    advance: (ms) => {
      now += ms;
    },
  };
}

function limited(header, body = {}) {
  return {
    ok: false,
    status: 429,
    headers: { get: (name) => (name === 'Retry-After' ? header : null) },
    json: async () => ({ group: 'payment', ...body }),
  };
}

test('429 is actionable, preserves status and never automatically retries a payment', async () => {
  const sent = [];
  const ui = common(async (path, request) => {
    sent.push(JSON.parse(request.body));
    if (sent.length === 1) return limited('5');
    return { ok: true, headers: { get: () => null }, json: async () => ({ status: 'Succeeded' }) };
  });
  const payload = { key: 'same-payment-key', number: 'demo' };
  await assert.rejects(
    ui.api('orders/1/pay/card', 'POST', payload),
    (error) =>
      error.status === 429 && error.retryAfterSeconds === 5 && /5 giây/.test(error.message),
  );
  await assert.rejects(
    ui.api('orders/1/pay/card', 'POST', payload),
    (error) => error.status === 429,
  );
  assert.equal(sent.length, 1);
  ui.advance(5001);
  assert.equal((await ui.api('orders/1/pay/card', 'POST', payload)).status, 'Succeeded');
  assert.deepEqual(sent[0], sent[1]);
});

test('polling cooldown ignores query changes and does not block another resource', async () => {
  let calls = 0;
  const ui = common(async (path) => {
    calls++;
    return path.includes('orders')
      ? limited(null, { retryAfterSeconds: 8, group: 'read' })
      : { ok: true, headers: { get: () => null }, json: async () => ({ available: 10 }) };
  });
  await assert.rejects(ui.api('orders/page?page=1'));
  await assert.rejects(ui.api('orders/page?page=2'));
  assert.equal(calls, 1);
  assert.equal((await ui.api('seller/finance')).available, 10);
});

test('429 handles HTTP-date and malformed non-JSON responses without leaking server HTML', async () => {
  const ui = common(async () => limited('Wed, 28 Oct 2026 03:00:12 GMT'));
  await assert.rejects(ui.api('orders'), (error) => error.retryAfterSeconds === 12);
  const fallback = common(async () => ({
    ...limited('bad-date'),
    json: async () => {
      throw new Error('<script>server HTML</script>');
    },
  }));
  await assert.rejects(
    fallback.api('orders'),
    (error) => error.retryAfterSeconds === 60 && !error.message.includes('<script>'),
  );
});

test('login cooldown disables submit, counts real elapsed time and permits manual retry only', () => {
  let now = 1000,
    tick,
    cleared = false;
  const notice = { dataset: { retryAfter: '3' }, hidden: true, textContent: '' };
  const button = { disabled: false, textContent: 'Đăng nhập' };
  const context = {
    document: { getElementById: () => notice, querySelector: () => button },
    Date: { now: () => now },
    setInterval: (callback) => {
      tick = callback;
      return 1;
    },
    clearInterval: () => {
      cleared = true;
    },
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/login-rate-limit.js', 'utf8'), context);
  assert.equal(button.disabled, true);
  assert.equal(notice.hidden, false);
  now += 3100;
  tick();
  assert.equal(button.disabled, false);
  assert.equal(button.textContent, 'Đăng nhập');
  assert.equal(cleared, true);
});
