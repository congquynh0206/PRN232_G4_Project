// Actual Razor, cookie session, proxy and JS; isolated API fixture, no SQL/providers.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const { chromium } = require('playwright');
const paged = { page: 1, pageSize: 10, totalCount: 0, items: [], counts: {}, openCount: 0,
  unreadCount: 0 };
const password = 'Mật khẩu 123456!';
const output = path.resolve('artifacts/password-confirmation-ui');

async function main() {
  fs.mkdirSync(output, { recursive: true });
  const attempts = [];
  let mutations = 0;
  const fixture = http.createServer(async (req, res) => {
    let raw = '';
    for await (const chunk of req) raw += chunk;
    const resource = req.url.split('?')[0];
    const body = JSON.parse(raw || '{}');
    res.setHeader('Content-Type', 'application/json');
    const reply = (status, data) => {
      res.statusCode = status;
      res.end(JSON.stringify(data));
    };
    if (resource === '/api/auth/login')
      return reply(200, {
        token: 'password-browser-fixture', userId: 21, username: 'QA xác nhận mật khẩu',
        role: body.email.startsWith('admin') ? 'admin' : 'seller',
        expiresAt: '2099-01-01T00:00:00Z',
      });
    if (req.method === 'POST') {
      assert.equal(req.headers.authorization, 'Bearer password-browser-fixture');
      const encoded = req.headers['x-action-password'];
      attempts.push({ resource, raw, confirmed: !!encoded });
      if (!encoded)
        return reply(428, { code: 'PasswordConfirmationRequired',
          action: resource.includes('payouts') ? 'Gửi yêu cầu rút tiền' : 'Thay đổi phí nền tảng' });
      const supplied = decodeURIComponent(encoded);
      if (supplied === 'limited') {
        res.setHeader('Retry-After', '1');
        return reply(429, { code: 'RateLimitExceeded', group: 'password-confirmation',
          retryAfterSeconds: 1 });
      }
      if (supplied !== password)
        return reply(403, { code: 'PasswordConfirmationInvalid', error: 'Mật khẩu không đúng.' });
      await new Promise((resolve) => setTimeout(resolve, 200));
      mutations++;
      return reply(200, { id: mutations, status: 'Created', amount: body.amount });
    }
    if (resource === '/api/seller/finance')
      return reply(200, { level: 3, processing: 0, available: 100, onHold: 0, negative: 0,
        payoutPending: 0, paidOut: 0, holdSeconds: 0, monthlySalesLimit: 1000000,
        monthlySalesAmount: 0, levelProgress: {} });
    if (resource === '/api/seller/debt')
      return reply(200, { balance: 0, sellerId: 21, accountStatus: 'Active' });
    if (resource === '/api/admin/platform-fees')
      return reply(200, { ...paged, latestPolicyId: 1,
        current: { id: 1, feePercent: 12, fixedFee: .3, effectiveAt: '2026-10-18T00:00:00Z' } });
    return reply(200, paged);
  });
  await new Promise((resolve) => fixture.listen(0, '127.0.0.1', resolve));
  const reserve = http.createServer();
  await new Promise((resolve) => reserve.listen(0, '127.0.0.1', resolve));
  const port = reserve.address().port;
  await new Promise((resolve) => reserve.close(resolve));
  const base = `http://127.0.0.1:${port}`;
  const frontend = spawn('dotnet', [path.resolve(process.env.FRONTEND_DLL ||
    'artifacts/password-confirmation-validation/build/frontend.dll'), '--urls', base], {
    cwd: path.resolve('frontend'), windowsHide: true,
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development',
      ApiSettings__BaseUrl: `http://127.0.0.1:${fixture.address().port}` },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  let logs = '', browser;
  frontend.stdout.on('data', (chunk) => { logs += chunk; });
  frontend.stderr.on('data', (chunk) => { logs += chunk; });
  try {
    for (let i = 0; ; i++) {
      try {
        if ((await fetch(`${base}/Account/Login`)).ok) break;
      } catch {}
      if (i > 100) throw new Error(logs);
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    browser = await chromium.launch({ headless: true,
      ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
    for (const width of [1440, 375]) {
      for (const role of ['seller', 'admin']) {
        const context = await browser.newContext({ viewport: { width, height: 900 },
          reducedMotion: 'reduce' });
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', (error) => errors.push(error.message));
        await page.route('**/Realtime/Session', (route) => route.fulfill({ status: 503, body: '{}' }));
        await page.goto(`${base}/Account/Login`);
        await page.locator('#email').fill(`${role}@password.test`);
        await page.locator('#password').fill(password);
        await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
        await page.waitForURL(new RegExp(`/${role}`, 'i'));
        await page.waitForFunction(() => !!window.G4?.PasswordConfirmation);
        await page.evaluate((tab) => G4.Navigation.select(tab), role === 'seller' ? 'finance' : 'platform-fees');
        await page.evaluate(async (role) => {
          if (role === 'admin') await G4.PlatformFees.load();
          else document.querySelector('[data-tab="finance"]').click();
        }, role);
        if (role === 'seller') await page.locator('#payout-amount').fill('12.5');
        else {
          await page.locator('#platform-fee-percent').fill('10');
          await page.locator('#platform-fee-fixed').fill('0.30');
          await page.locator('#platform-fee-reason').fill('QA <b>không phải HTML</b>');
        }
        const trigger = page.locator(role === 'seller' ? '#request-payout' : '#save-platform-fee');
        const dialog = page.locator('#password-confirmation-dialog');
        const input = page.locator('#password-confirmation-input');
        const confirm = page.locator('#password-confirmation-submit');
        const before = mutations;
        await trigger.click();
        await dialog.waitFor({ state: 'visible' });
        assert.equal(await input.evaluate((e) => document.activeElement === e), true);
        assert.equal(mutations, before);
        await input.fill('temporary-secret');
        await page.keyboard.press('Escape');
        await dialog.waitFor({ state: 'hidden' });
        assert.equal(await input.inputValue(), '');
        assert.equal(mutations, before);
        await trigger.waitFor({ state: 'visible' });
        // Cancellation is definite: the next request may change amount safely.
        if (role === 'seller') await page.locator('#payout-amount').fill('15');
        const start = attempts.length;
        await trigger.click();
        await dialog.waitFor({ state: 'visible' });
        await input.fill('wrong');
        await confirm.click();
        await page.locator('#password-confirmation-error').filter({ hasText: 'Mật khẩu không đúng' }).waitFor();
        assert.equal(await input.inputValue(), '');
        assert.equal(await input.getAttribute('aria-invalid'), 'true');
        assert.equal(mutations, before);
        // 429 stays in the dialog, counts down, and never automatically posts.
        await input.fill('limited');
        await confirm.click();
        await page.waitForFunction(() => document.getElementById('password-confirmation-submit').disabled);
        await page.waitForFunction(() => !document.getElementById('password-confirmation-submit').disabled);
        assert.equal(mutations, before);
        await input.fill(password);
        assert.equal(await dialog.locator('b').count(), 0);
        assert.equal(await dialog.evaluate((e) => e.scrollWidth > e.clientWidth), false);
        await page.screenshot({ path: path.join(output, `${role}-${width}-confirm.png`), fullPage: true });
        await confirm.click();
        assert.equal(await confirm.isDisabled(), true);
        assert.equal(await page.locator('#password-confirmation-cancel').isDisabled(), true);
        await page.keyboard.press('Escape');
        assert.equal(await dialog.isVisible(), true);
        await dialog.waitFor({ state: 'hidden' });
        assert.equal(mutations, before + 1);
        assert.equal(await input.inputValue(), '');
        const sent = attempts.slice(start);
        assert.equal(sent.length, 4);
        assert.ok(sent.every((entry) => entry.raw === sent[0].raw), 'stable reviewed payload and key');
        // Closing confirmation must leave an existing underlying dialog open.
        await page.evaluate((role) => {
          document.getElementById('detail-dialog').showModal();
          window.stackedResult = G4.api(role === 'seller' ? 'seller/payouts' : 'admin/platform-fees',
            'POST', {}).then(() => 'unexpected', (error) => error.code);
        }, role);
        await dialog.waitFor({ state: 'visible' });
        await page.keyboard.press('Escape');
        assert.equal(await page.evaluate(() => window.stackedResult), 'PasswordConfirmationCancelled');
        assert.equal(await page.locator('#detail-dialog').isVisible(), true);
        assert.deepEqual(errors, []);
        await context.close();
      }
    }
    // No credential values may appear in server logging.
    assert.ok(!logs.includes(password) && !logs.includes('temporary-secret'));
    console.log('Password confirmation browser passed: Razor/proxy, seller payout/admin fee, ' +
      'desktop/mobile, Escape/cancel, wrong password, 429/manual retry, Unicode, pending state, ' +
      'one mutation, preserved payload/key, cleared password and escaped summary.');
  } finally {
    if (browser) await browser.close();
    frontend.kill();
    await new Promise((resolve) => fixture.close(resolve));
  }
}
main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
