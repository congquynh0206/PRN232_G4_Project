const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function api(error) {
  const context = {
    window: {},
    URL,
    setTimeout,
    clearTimeout,
    fetch: async () => ({
      ok: false,
      status: 409,
      headers: { get: () => null },
      json: async () => ({ error }),
    }),
    document: { getElementById: () => ({ textContent: '', innerHTML: '' }) },
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/common.js', 'utf8'), context);
  return context.window.G4.api('orders/1/pay/card', 'POST', { key: 'k' });
}

test('monthly sales limit tells the buyer the shop is capped, not about withdrawals', async () => {
  await assert.rejects(
    api('Seller level 1 monthly sales limit would be exceeded'),
    (error) =>
      error.status === 409
      && /hạn mức doanh số tháng/.test(error.message)
      && !/rút/.test(error.message),
  );
});

test('payout and balance errors keep the withdrawal message', async () => {
  await assert.rejects(
    api('Insufficient available balance for payout'),
    (error) => /Chưa thể rút số tiền này/.test(error.message),
  );
});
