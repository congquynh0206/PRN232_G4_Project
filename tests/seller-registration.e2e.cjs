// Explicit opt-in: uses only the dedicated QA app and QA SQL database.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { spawnSync } = require('node:child_process');
const { chromium } = require('playwright');
const base = process.env.G4_REGISTRATION_E2E_URL;
if (base !== 'http://127.0.0.1:5133')
  throw new Error('Use the dedicated QA frontend at http://127.0.0.1:5133');
const api = 'http://127.0.0.1:5253/api';
const folder = 'artifacts/seller-registration-qa';
(async () => {
  await fs.mkdir(folder, { recursive: true });
  let browser;
  try {
    browser = await chromium.launch({ headless: true });
  } catch {
    browser = await chromium.launch({ headless: true, channel: 'msedge' });
  }
  try {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', (error) => errors.push(error.message));
    await page.goto(base + '/Account/RegisterSeller');
    await page.getByRole('button', { name: 'Định danh không hợp lệ', exact: true }).click();
    await page.waitForFunction(
      () => document.getElementById('registration-identity').value === '1234',
    );
    await page.getByRole('button', { name: 'Đăng ký người bán', exact: true }).click();
    await page
      .locator('#registration-error')
      .filter({ hasText: 'Xác minh giả lập thất bại' })
      .waitFor();
    await page.getByRole('button', { name: 'Trùng blacklist', exact: true }).click();
    await page.waitForFunction(
      () => document.getElementById('registration-identity').value === '999000000002',
    );
    await page.getByRole('button', { name: 'Đăng ký người bán', exact: true }).click();
    await page.locator('#registration-error').filter({ hasText: 'Không thể đăng ký' }).waitFor();
    assert.equal(await page.locator('#registration-success').isVisible(), false);
    await page.screenshot({ path: folder + '/registration-blocked.jpg', fullPage: true });
    console.log('E2E: invalid identity and blacklist rejected before registration.');
    await page.getByRole('button', { name: 'Seller mới hợp lệ', exact: true }).click();
    await page.waitForFunction(
      () =>
        document.getElementById('registration-identity').value.length === 12 &&
        document.getElementById('registration-identity').value !== '999000000002',
    );
    const email = await page.locator('#registration-email').inputValue();
    const originalIdentity = await page.locator('#registration-identity').inputValue();
    await page.screenshot({ path: folder + '/registration-form.jpg', fullPage: true });
    await page.getByRole('button', { name: 'Đăng ký người bán', exact: true }).click();
    await page.locator('#registration-success').filter({ hasText: 'số dư 0' }).waitFor();
    const login = await context.request.post(api + '/auth/login', {
      data: { email, password: 'DemoSeller123!' },
    });
    assert.equal(login.status(), 200);
    const auth = await login.json();
    const headers = { Authorization: 'Bearer ' + auth.token };
    let state = await (await context.request.get(api + '/seller/debt', { headers })).json();
    assert.equal(state.balance, 0);
    assert.equal(state.permanentSalesBlockedAt, null);
    await fs.writeFile(
      folder + '/registered-seller.json',
      JSON.stringify({ sellerId: auth.userId, email }, null, 2),
    );
    await page.goto(base + '/Account/Login');
    await page.getByLabel('Địa chỉ thư điện tử', { exact: true }).fill(email);
    await page.getByLabel('Mật khẩu', { exact: true }).fill('DemoSeller123!');
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await page.getByRole('button', { name: 'Định danh', exact: true }).click();
    await page
      .locator('#seller-identity-history')
      .filter({ hasText: 'Định danh hiện tại' })
      .waitFor();
    assert.equal(
      (await page.locator('#seller-identity-history').innerText()).includes(originalIdentity),
      false,
    );
    await page.locator('#seller-identity-kind').selectOption('BANK');
    await page.locator('#seller-identity-bank').fill('QABANK');
    await page.locator('#seller-identity-value').fill('000' + String(Date.now()));
    await page.getByRole('button', { name: 'Xác minh và cập nhật', exact: true }).click();
    await page.locator('#seller-identity-result').filter({ hasText: 'Đã xác minh' }).waitFor();
    assert.equal(await page.locator('#seller-identity-history article').count(), 2);
    console.log(
      'E2E: clean seller activates, logs in, and changes verified identity without losing history.',
    );
    assert.ok(Number.isSafeInteger(auth.userId));
    const sql = spawnSync(
      'sqlcmd',
      [
        '-S',
        'localhost',
        '-E',
        '-C',
        '-d',
        'G4_SellerRegistration_Test_20261015',
        '-b',
        '-Q',
        `UPDATE SellerAccount SET NegativeBalance=40 WHERE SellerId=${auth.userId} AND NegativeBalance=0 AND DebtStartedAt IS NULL;`,
      ],
      { encoding: 'utf8' },
    );
    assert.equal(sql.status, 0, sql.stderr);
    await page.locator('[data-tab="finance"]').click();
    await page.locator('#seller-debt-summary').filter({ hasText: '$40.00' }).waitFor();
    console.log('E2E: waiting for background worker using shortened QA policy (90 seconds).');
    const deadline = Date.now() + 120000;
    while (Date.now() < deadline) {
      state = await (await context.request.get(api + '/seller/debt', { headers })).json();
      if (state.permanentSalesBlockedAt) break;
      await page.waitForTimeout(2000);
    }
    assert.ok(state.permanentSalesBlockedAt, 'Worker did not classify overdue inactive debt');
    assert.equal(state.balance, 40);
    await page.getByRole('button', { name: 'Làm mới', exact: true }).click();
    await page.locator('#seller-debt-summary').filter({ hasText: 'Khóa bán vĩnh viễn' }).waitFor();
    await page.getByRole('button', { name: 'Điền toàn bộ số nợ', exact: true }).click();
    await page.getByRole('button', { name: 'Trả nợ', exact: true }).click();
    await page.locator('#debt-repayment-result').filter({ hasText: '$0.00' }).waitFor();
    state = await (await context.request.get(api + '/seller/debt', { headers })).json();
    assert.equal(state.balance, 0);
    assert.ok(state.permanentSalesBlockedAt);
    const payout = await context.request.post(api + '/seller/payouts', {
      headers,
      data: { amount: 1, key: 'post-writeoff-' + auth.userId },
    });
    assert.equal(payout.status(), 409);
    await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
    await page.goto(base + '/Account/RegisterSeller');
    await page.getByRole('button', { name: 'Seller mới hợp lệ', exact: true }).click();
    await page.waitForFunction(() =>
      document.getElementById('registration-email').value.includes('@example.test'),
    );
    await page.locator('#registration-identity').fill(originalIdentity);
    await page.getByRole('button', { name: 'Đăng ký người bán', exact: true }).click();
    await page.locator('#registration-error').filter({ hasText: 'Không thể đăng ký' }).waitFor();
    console.log(
      'E2E: automatic write-off keeps original debt; repayment recovers debt without unblocking; re-registration with old identity rejected.',
    );
    await page.goto(base + '/Account/Login');
    await page.getByLabel('Địa chỉ thư điện tử', { exact: true }).fill('admin@example.test');
    await page.getByLabel('Mật khẩu', { exact: true }).fill('G4@123456');
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await page.locator('[data-tab="seller-risk"]').click();
    await page.locator('#seller-risk-seller').fill(String(auth.userId));
    await page.getByRole('button', { name: 'Lọc hồ sơ', exact: true }).click();
    await page
      .locator('#seller-write-offs')
      .filter({ hasText: 'Seller #' + auth.userId })
      .waitFor();
    await page.waitForFunction(() =>
      document
        .getElementById('seller-write-offs')
        .innerText.includes('Đã thu hồi sau quyết định: $40.00'),
    );
    assert.equal(await page.locator('#seller-blacklist article').count(), 2);
    assert.equal(
      (await page.locator('#seller-blacklist').innerText()).includes(originalIdentity),
      false,
    );
    await page.screenshot({ path: folder + '/admin-writeoff.jpg', fullPage: true });
    await page.goto(base + '/Account/RegisterSeller');
    await page.setViewportSize({ width: 390, height: 844 });
    assert.ok(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
      'Registration overflows mobile width',
    );
    await page.screenshot({ path: folder + '/registration-mobile.jpg', fullPage: true });
    assert.deepEqual(errors, [], 'Page JavaScript error');
    console.log(
      'E2E passed: registration scenarios, identity history, automatic write-off, blacklist, recovery report, permanent restrictions and mobile layout.',
    );
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
