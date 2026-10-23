// Dedicated QA only: API 5259, frontend 5139, database G4_N1_Test_20261017.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const base = process.env.G4_N1_E2E_URL,
  api = 'http://127.0.0.1:5259/api';
if (base !== 'http://127.0.0.1:5139')
  throw new Error('Use dedicated N1 QA frontend http://127.0.0.1:5139');
const folder = 'artifacts/n1-notifications-qa';
(async () => {
  const fixture = JSON.parse(await fs.readFile(folder + '/fixture.json', 'utf8'));
  let browser;
  try {
    browser = await chromium.launch({ headless: true });
  } catch {
    browser = await chromium.launch({ headless: true, channel: 'msedge' });
  }
  const errors = [];
  async function login(email, role) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } }),
      page = await context.newPage();
    page.on('pageerror', (e) => errors.push(e.message));
    await page.goto(base + '/Account/Login');
    await page.locator('#email').fill(email);
    await page.locator('#password').fill('NoticeDemo123!');
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await page.waitForURL(`**/${role}`);
    const response = await context.request.post(api + '/auth/login', {
      data: { email, password: 'NoticeDemo123!' },
    });
    assert.equal(response.status(), 200);
    return { context, page, headers: { Authorization: 'Bearer ' + (await response.json()).token } };
  }
  async function filter(p, eventType) {
    await p.locator('[name=eventType]').selectOption(eventType);
    await p.getByRole('button', { name: 'Lọc email', exact: true }).click();
    await p.waitForFunction(
      () => document.getElementById('notification-list').getAttribute('aria-busy') === 'false',
    );
  }
  try {
    const buyer = await login(fixture.buyerEmail, 'Buyer'),
      seller = await login(fixture.sellerEmail, 'Seller'),
      admin = await login(fixture.adminEmail, 'Admin');
    const b = buyer.page,
      s = seller.page,
      a = admin.page;
    await b.locator('[data-tab=notifications]').click();
    await filter(b, 'PaymentFailed');
    assert.equal(await b.locator('.diagnostic-card').count(), 2);
    await b.locator('[data-open-notification]').first().click();
    await b.locator('.email-text').filter({ hasText: 'Thẻ giả lập bị từ chối' }).waitFor();
    await b.screenshot({ path: folder + '/buyer-payment-failed-desktop.png', fullPage: true });
    await b.locator('#detail-close').click();
    await filter(b, 'ReturnRejected');
    await b.locator('[data-open-notification]').click();
    await b.locator('.email-text').filter({ hasText: '<img src=x onerror=bad()>' }).waitFor();
    assert.equal(await b.locator('#detail-content img[onerror]').count(), 0);
    await b.setViewportSize({ width: 390, height: 844 });
    await b.screenshot({ path: folder + '/buyer-return-rejected-mobile.png', fullPage: true });
    assert.equal(
      await b.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1),
      true,
    );
    await b.locator('#detail-close').click();
    console.log(
      'N1 E2E: buyer filters failed payments and return rejection; reason encoded; desktop/mobile render without overflow.',
    );

    await s.locator('[data-tab=notifications]').click();
    await filter(s, 'CancellationRequested');
    assert.equal(await s.locator('.diagnostic-card').count(), 1);
    await s.locator('[data-open-notification]').click();
    await s.locator('.email-text').filter({ hasText: 'duyệt hoặc từ chối' }).waitFor();
    await s
      .locator('.email-text')
      .filter({ hasText: base + '/Seller?orderId=' + fixture.orderId })
      .waitFor();
    await s.locator('#detail-close').click();
    await filter(s, 'PayoutReturned');
    await s.locator('[data-open-notification]').click();
    await s.getByRole('link', { name: 'Xem tài chính và lịch sử rút tiền', exact: true }).waitFor();
    assert.match(await s.locator('#detail-content').innerText(), /Lần rút tiền #/);
    await s.screenshot({ path: folder + '/seller-payout-returned-desktop.png', fullPage: true });
    await s.getByRole('link', { name: 'Xem tài chính và lịch sử rút tiền', exact: true }).click();
    await s.locator('#finance.active').waitFor();
    await s.goto(base + '/Seller?orderId=' + fixture.orderId);
    await s.locator('[data-order-detail="' + fixture.orderId + '"]').waitFor();
    console.log(
      'N1 E2E: seller gets actionable cancellation, payout reference and working finance/order deep links.',
    );

    await a.locator('[data-tab=inbox]').click();
    await filter(a, 'PayoutCompleted');
    await a
      .getByRole('heading', {
        name: 'Rút tiền hoàn tất — Lần rút tiền #' + fixture.payoutId,
        exact: true,
      })
      .waitFor();
    const failures = await buyer.context.request.get(
      api + '/notifications?eventType=PaymentFailed',
      { headers: buyer.headers },
    );
    const failureData = await failures.json(),
      unpaid = failureData.items[0].orderId;
    for (let i = 0; i < 2; i++)
      assert.equal(
        (
          await buyer.context.request.post(`${api}/orders/${unpaid}/cancel`, {
            headers: buyer.headers,
          })
        ).status(),
        200,
      );
    const cancel = await buyer.context.request.get(
      `${api}/notifications?eventType=OrderCancelled&orderId=${unpaid}`,
      { headers: buyer.headers },
    );
    assert.equal((await cancel.json()).totalCount, 1);
    await b.locator('[data-tab=notifications]').click();
    await filter(b, 'OrderCancelled');
    await b.locator('.diagnostic-card').filter({ hasText: 'Đã hủy đơn hàng' }).waitFor();
    assert.equal(
      (
        await seller.context.request.get(api + '/notifications/' + fixture.rejectedMailId, {
          headers: seller.headers,
        })
      ).status(),
      404,
    );
    assert.equal(
      (
        await seller.context.request.get(api + '/notifications?eventType=PaymentFailed', {
          headers: seller.headers,
        })
      ).status(),
      200,
    );
    assert.equal(
      (
        await (
          await seller.context.request.get(api + '/notifications?eventType=PaymentFailed', {
            headers: seller.headers,
          })
        ).json()
      ).totalCount,
      0,
    );
    assert.deepEqual(errors, []);
    console.log(
      'N1 E2E passed: admin filters new events, actual cancellation emits once on retry, recipient-scoped list/detail and no JS errors.',
    );
  } finally {
    await browser.close();
  }
})().catch((e) => {
  console.error(e);
  process.exitCode = 1;
});
