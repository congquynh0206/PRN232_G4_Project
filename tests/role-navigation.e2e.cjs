// Real Razor frontend + isolated login/API fixtures. No database or provider calls.
// Build frontend first. Run with NODE_PATH pointing to the bundled Playwright package.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const { chromium } = require('playwright');
const output = path.resolve('artifacts/role-navigation-ui');
const paged = {
  page: 1,
  pageSize: 10,
  totalCount: 0,
  items: [],
  counts: { approval: 2, receipt: 0 },
  openCount: 3,
};
function response(route) {
  const resource = new URL(route).pathname.replace('/api/proxy/', '');
  if (resource === 'addresses') return [];
  if (resource === 'cart')
    return {
      items: [
        {
          productId: 1,
          sellerId: 2,
          sellerName: 'Cửa hàng thử nghiệm',
          title: 'Sản phẩm thử nghiệm',
          quantity: 1,
          available: 10,
          price: 25,
          weightKg: 0.5,
        },
      ],
      version: 1,
    };
  if (resource === 'seller/finance')
    return {
      level: 3,
      processing: 0,
      available: 87.7,
      onHold: 0,
      negative: 0,
      payoutPending: 0,
      paidOut: 0,
      holdSeconds: 0,
      monthlySalesLimit: 10000,
      monthlySalesAmount: 100,
      levelProgress: {},
    };
  if (resource === 'seller/debt') return { balance: 0, sellerId: 2, accountStatus: 'Active' };
  if (resource === 'seller/level')
    return {
      sellerId: 2,
      level: 1,
      history: [],
      preview: {
        previousLevel: 1,
        resultLevel: 1,
        reason: 'Chưa đủ dữ liệu',
        tiers: [],
        metrics: {},
        policy: {},
      },
    };
  if (resource === 'seller/finance/report')
    return { ...paged, totals: {}, chart: [], unreconciledPayments: 0, unreconciledRefunds: 0 };
  if (resource === 'seller/product-feedback/products')
    return { ...paged, summary: { productCount: 0, reviewCount: 0, unansweredCount: 0 } };
  if (resource === 'buyer/transactions')
    return { ...paged, items: [], periodTotals: { paid: 0, refunded: 0 }, orderTotals: [] };
  if (resource === 'admin/platform-fees')
    return {
      ...paged,
      current: { id: 1, feePercent: 12, fixedFee: 0.3, effectiveAt: '2026-10-18T00:00:00Z' },
      latestPolicyId: 1,
    };
  if (resource === 'admin/seller-write-offs')
    return {
      ...paged,
      policy: { enabled: true, afterSeconds: 300, inactivitySeconds: 60 },
      summary: { originalAmount: 0, recoveredAmount: 0 },
    };
  return paged;
}
async function main() {
  fs.mkdirSync(output, { recursive: true });
  const loginApi = http.createServer(async (req, res) => {
    if (req.url !== '/api/auth/login') {
      res.writeHead(404);
      res.end();
      return;
    }
    let body = '';
    for await (const chunk of req) body += chunk;
    const role = JSON.parse(body).email.split('@')[0];
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(
      JSON.stringify({
        token: 'navigation-fixture-only',
        userId: 1,
        username: 'Tài khoản kiểm thử',
        role,
        expiresAt: '2099-01-01T00:00:00Z',
      }),
    );
  });
  await new Promise((resolve) => loginApi.listen(0, '127.0.0.1', resolve));
  const reserve = http.createServer();
  await new Promise((resolve) => reserve.listen(0, '127.0.0.1', resolve));
  const port = reserve.address().port;
  await new Promise((resolve) => reserve.close(resolve));
  const base = `http://127.0.0.1:${port}`;
  const dll = process.env.FRONTEND_DLL
    ? path.resolve(process.env.FRONTEND_DLL)
    : 'bin/Debug/net8.0/frontend.dll';
  const frontend = spawn('dotnet', [dll, '--urls', base], {
    cwd: path.resolve('frontend'),
    windowsHide: true,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ApiSettings__BaseUrl: `http://127.0.0.1:${loginApi.address().port}`,
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  let serverOutput = '';
  frontend.stdout.on('data', (chunk) => (serverOutput += chunk));
  frontend.stderr.on('data', (chunk) => (serverOutput += chunk));
  let browser;
  try {
    for (let attempt = 0; ; attempt++) {
      let status;
      try {
        status = (await fetch(`${base}/Account/Login`)).status;
        if (status === 200) break;
      } catch (error) {
        status = `${error.message}: ${error.cause?.message}`;
      }
      if (attempt >= 100) throw new Error(`Frontend did not start (${status}): ${serverOutput}`);
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    browser = await chromium.launch({
      headless: true,
      ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}),
    });
    for (const role of ['buyer', 'seller', 'admin', 'shipper']) {
      const context = await browser.newContext({
        viewport: { width: 1440, height: 1000 },
        timezoneId: 'Asia/Ho_Chi_Minh',
      });
      const page = await context.newPage(),
        errors = [],
        mutations = [];
      page.on('pageerror', (error) => errors.push(error.message));
      await page.route('**/Realtime/Session', (route) =>
        route.fulfill({ status: 503, body: '{}' }),
      );
      await page.route('**/api/proxy/**', (route) => {
        if (route.request().method() !== 'GET') mutations.push(route.request().url());
        return route.fulfill({ json: response(route.request().url()) });
      });
      await page.goto(`${base}/Account/Login`);
      await page.locator('#email').fill(`${role}@navigation.test`);
      await page.locator('#password').fill('fixture-password');
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
      await page.waitForURL(new RegExp(`/${role}`, 'i'));
      if (role === 'shipper') {
        assert.equal(await page.locator('[data-role-navigation]').count(), 0);
        assert.equal(await page.locator('[data-shipment-filter]').count(), 2);
        assert.equal(await page.locator('script[src*="role-navigation"]').count(), 0);
        await context.close();
        continue;
      }
      const nav = page.locator('[data-role-navigation]');
      const expected = role === 'buyer' ? 5 : role === 'seller' ? 12 : 9;
      await page.waitForFunction(() => !!G4.Navigation.current);
      assert.equal(await nav.locator('[data-tab]').count(), expected);
      assert.equal(await nav.locator('[data-tab=checkout]').count(), 0);
      const ids = await nav
        .locator('[data-tab]')
        .evaluateAll((nodes) => nodes.map((n) => n.dataset.tab));
      // Every menu entry activates its real panel and keeps the badge DOM unique.
      for (const id of ids) {
        await nav.locator(`[data-tab="${id}"]`).click();
        await page.waitForFunction(
          (id) => document.querySelector('.tab-panel.active')?.id === id,
          id,
        );
        assert.doesNotMatch(
          await page.locator('#message').textContent(),
          /Cannot read|not a function|is not defined|undefined/i,
          `${role}/${id} loader error`,
        );
        assert.equal(new URL(page.url()).hash, `#${id}`);
        assert.equal(await page.locator('.tab-panel.active').count(), 1);
        assert.equal(await nav.locator('[aria-current=page]').count(), 1);
      }
      const first = ids[0],
        second = ids[1];
      await nav.locator(`[data-tab="${first}"]`).click();
      await nav.locator(`[data-tab="${second}"]`).click();
      await page.goBack();
      await page.waitForFunction((id) => G4.Navigation.current === id, first);
      await page.goForward();
      await page.waitForFunction((id) => G4.Navigation.current === id, second);
      await page.reload();
      await page.waitForFunction((id) => G4.Navigation.current === id, second);
      assert.equal(await page.locator('.tab-panel.active').getAttribute('id'), second);
      await page.goto(`${base}/${role}?tab=${second}`);
      await page.waitForFunction((id) => G4.Navigation.current === id, second);
      await page.screenshot({ path: path.join(output, `${role}-desktop.png`), fullPage: true });
      for (const width of [1024, 390]) {
        await page.setViewportSize({ width, height: 844 });
        for (const id of ids) {
          if (role !== 'buyer' && width <= 1000) await page.locator('#navigation-toggle').click();
          await nav.locator(`[data-tab="${id}"]`).click();
          await page.waitForFunction((id) => G4.Navigation.current === id, id);
          assert.equal(
            await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
            true,
            `${role}/${id} overflow at ${width}`,
          );
        }
      }
      for (const width of [1024, 1000, 768, 390, 320]) {
        await page.setViewportSize({ width, height: 844 });
        assert.equal(
          await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
          true,
          `${role} overflow at ${width}`,
        );
        if (role !== 'buyer' && width <= 1000) {
          await page.locator('#navigation-toggle').click();
          assert.equal(await page.locator('#navigation-drawer').evaluate((el) => el.open), true);
          assert.equal(
            await page.locator('#navigation-toggle').getAttribute('aria-expanded'),
            'true',
          );
          assert.equal(await page.locator('#dispute-tab-count').count(), 1);
          assert.equal(
            await page.evaluate(() =>
              document.querySelector('#navigation-drawer').contains(document.activeElement),
            ),
            true,
          );
          await page.keyboard.press('Shift+Tab');
          await page.keyboard.press('Tab');
          assert.equal(
            await page.evaluate(() =>
              document.querySelector('#navigation-drawer').contains(document.activeElement),
            ),
            true,
          );
          await page.keyboard.press('Escape');
          await page.waitForFunction(() => !document.querySelector('#navigation-drawer').open);
          await page.waitForFunction(
            () =>
              document.querySelector('#navigation-toggle').getAttribute('aria-expanded') ===
              'false',
          );
          assert.equal(
            await page.locator('#navigation-toggle').getAttribute('aria-expanded'),
            'false',
          );
          assert.equal(
            await page
              .locator('#navigation-toggle')
              .evaluate((el) => el === document.activeElement),
            true,
          );
          await page.locator('#navigation-toggle').click();
          await nav.locator(`[data-tab="${first}"]`).click();
          assert.equal(await page.locator('#navigation-drawer').evaluate((el) => el.open), false);
          await page.waitForFunction((id) => G4.Navigation.current === id, first);
        }
      }
      await page.setViewportSize({ width: 390, height: 844 });
      if (role === 'buyer') {
        assert.equal(await nav.evaluate((el) => getComputedStyle(el).position), 'fixed');
        await nav.locator('[data-tab=transactions]').click();
        await page.waitForFunction(() => G4.Navigation.current === 'transactions');
        assert.equal(await page.locator('#transaction-filters').count(), 1);
        assert.equal(await page.locator('#transaction-dialog').count(), 1);
        await nav.locator('[data-tab=summary]').click();
        await page.locator('#continue').click();
        await page.waitForFunction(() => G4.Navigation.current === 'checkout');
        // No address is seeded: the existing address manager opens on Continue.
        await page.keyboard.press('Escape');
        assert.equal(await nav.locator('[data-tab=summary]').getAttribute('aria-current'), 'page');
        await page.reload();
        await page.waitForFunction(() => G4.Navigation.current === 'checkout');
        await page.locator('[data-nav-target=summary]').click();
        await page.waitForFunction(() => G4.Navigation.current === 'summary');
      } else {
        await page.locator('#navigation-toggle').click();
        await page.screenshot({
          path: path.join(output, `${role}-mobile-menu.png`),
          fullPage: true,
        });
        // Resizing an open drawer must restore the same live navigation on desktop.
        await page.setViewportSize({ width: 1440, height: 1000 });
        await page.waitForFunction(() => !document.querySelector('#navigation-drawer').open);
        assert.equal(await page.locator('#workspace-sidebar [data-role-navigation]').count(), 1);
        await page.setViewportSize({ width: 390, height: 844 });
      }
      await page.screenshot({ path: path.join(output, `${role}-mobile.png`), fullPage: true });
      // Invalid/cross-role hashes fall back to an allowed panel without executing a loader for them.
      await page.goto(`${base}/${role}#not-a-panel`);
      await page.waitForFunction((id) => G4.Navigation.current === id, first);
      await page.waitForTimeout(150);
      assert.deepEqual(errors, [], `${role} JavaScript errors`);
      assert.deepEqual(mutations, [], `${role} navigation must not mutate business data`);
      await context.close();
      console.log(
        `PASS ${role}: all entries, history, reload, query links, invalid hashes, 320–1440px, mobile drawer/bottom navigation, no mutations or JS errors`,
      );
    }
    console.log(`Screenshots: ${output}. API/login fixtures only; no SQL/provider calls.`);
  } finally {
    await browser?.close();
    frontend.kill();
    await new Promise((resolve) => loginApi.close(resolve));
  }
}
main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
