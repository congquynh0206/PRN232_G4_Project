// Real MVC/Razor, cookie login, proxy and browser JS; isolated backend fixture, no SQL/provider.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const { chromium } = require('playwright');

async function main() {
  const output = path.resolve('artifacts/rate-limit-ui');
  fs.mkdirSync(output, { recursive: true });
  let loginCalls = 0,
    paymentCalls = 0;
  const clientIps = [],
    paymentBodies = [];
  const fixture = http.createServer(async (req, res) => {
    let body = '';
    for await (const chunk of req) body += chunk;
    res.setHeader('Content-Type', 'application/json');
    if (req.url === '/api/auth/login') {
      loginCalls++;
      clientIps.push(req.headers['x-forwarded-for']);
      const data = JSON.parse(body);
      if (data.email === 'backend-limited@example.test') {
        res.writeHead(429, { 'Retry-After': '2' });
        return res.end(JSON.stringify({ error: 'Bạn thao tác quá nhanh.', retryAfterSeconds: 2 }));
      }
      if (data.password === 'wrong') {
        res.statusCode = 401;
        return res.end('{}');
      }
      return res.end(
        JSON.stringify({
          token: 'browser-fixture-only',
          userId: 11,
          username: 'Tài khoản kiểm thử',
          role: 'buyer',
          expiresAt: '2099-01-01T00:00:00Z',
        }),
      );
    }
    if (req.url === '/api/test-payment') {
      paymentCalls++;
      paymentBodies.push(body);
      assert.equal(req.headers.authorization, 'Bearer browser-fixture-only');
      res.writeHead(429, { 'Retry-After': '2' });
      return res.end(
        JSON.stringify({ code: 'RateLimitExceeded', group: 'payment', retryAfterSeconds: 2 }),
      );
    }
    res.end('{}');
  });
  await new Promise((resolve) => fixture.listen(0, '127.0.0.1', resolve));
  const reserve = http.createServer();
  await new Promise((resolve) => reserve.listen(0, '127.0.0.1', resolve));
  const port = reserve.address().port;
  await new Promise((resolve) => reserve.close(resolve));
  const base = `http://127.0.0.1:${port}`;
  const dll = path.resolve(
    process.env.FRONTEND_DLL || 'artifacts/rate-limit-validation/build/frontend.dll',
  );
  const frontend = spawn('dotnet', [dll, '--urls', base], {
    cwd: path.resolve('frontend'),
    windowsHide: true,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ApiSettings__BaseUrl: `http://127.0.0.1:${fixture.address().port}`,
      RateLimiting__login__PermitLimit: '2',
      RateLimiting__login__WindowSeconds: '10',
      RateLimiting__login__SegmentsPerWindow: '2',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  let logs = '',
    browser;
  frontend.stdout.on('data', (chunk) => {
    logs += chunk;
  });
  frontend.stderr.on('data', (chunk) => {
    logs += chunk;
  });
  try {
    for (let attempt = 0; ; attempt++) {
      try {
        if ((await fetch(`${base}/Account/Login`)).status === 200) break;
      } catch {}
      if (attempt === 100) throw new Error(`MVC did not start: ${logs}`);
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    browser = await chromium.launch({
      headless: true,
      ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}),
    });
    const context = await browser.newContext({
      viewport: { width: 1280, height: 900 },
      extraHTTPHeaders: { 'X-Forwarded-For': '203.0.113.250' },
    });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', (error) => errors.push(error.message));
    await page.goto(`${base}/Account/Login`);
    const submit = async (email, password) => {
      await page.locator('#email').fill(email);
      await page.locator('#password').fill(password);
      const response = page.waitForResponse(
        (r) =>
          ['/', '/Account/Login'].includes(new URL(r.url()).pathname) &&
          r.request().method() === 'POST',
      );
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
      return response;
    };
    // Backend rejection must not appear as an incorrect-password error.
    let reply = await submit('backend-limited@example.test', 'fixture-password');
    assert.equal(reply.status(), 429);
    assert.equal(reply.headers()['retry-after'], '2');
    await page.locator('#login-rate-limit').waitFor({ state: 'visible' });
    assert.match(await page.locator('.validation').innerText(), /thao tác quá nhanh/);
    assert.doesNotMatch(await page.locator('.validation').innerText(), /mật khẩu không đúng/);
    assert.equal(await page.locator('button[type="submit"]').isDisabled(), true);
    await page.screenshot({
      path: path.join(output, 'login-backend-429-desktop.png'),
      fullPage: true,
    });
    assert.ok(clientIps.every((ip) => ip === '127.0.0.1' || ip === '::ffff:127.0.0.1'));
    await page.waitForFunction(
      () => !document.querySelector('.login-form button[type="submit"]').disabled,
    );
    // Allow the middleware's shared replenishment timer to tick before a fresh scenario.
    await page.waitForTimeout(600);
    await page.goto(`${base}/Account/Login`);
    // Use fetch with the real antiforgery token for rapid consecutive requests.
    const statuses = await page.evaluate(async () => {
      const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
      const result = [];
      for (let i = 0; i < 3; i++) {
        const response = await fetch('/Account/Login', {
          method: 'POST',
          body: new URLSearchParams({
            email: 'buyer@example.test',
            password: 'wrong',
            __RequestVerificationToken: token,
          }),
        });
        result.push({ status: response.status, text: await response.text() });
      }
      document.open();
      document.write(result[2].text);
      document.close();
      return result.map((r) => r.status);
    });
    assert.deepEqual(statuses, [200, 429, 429]);
    assert.equal(
      loginCalls,
      2,
      'the earlier attempt consumed one slot; MVC rejects excess before backend',
    );
    await page.setViewportSize({ width: 390, height: 844 });
    await page.locator('#login-rate-limit').waitFor({ state: 'visible' });
    assert.equal(
      await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
      true,
    );
    await page.screenshot({
      path: path.join(output, 'login-local-429-mobile.png'),
      fullPage: true,
    });
    await page.waitForFunction(
      () => !document.querySelector('.login-form button[type="submit"]').disabled,
    );
    await page.waitForTimeout(600);
    await page.route('**/Realtime/Session', (route) => route.fulfill({ status: 503, json: {} }));
    await page.route('**/api/proxy/**', (route) => {
      if (route.request().url().includes('/test-payment')) return route.continue();
      const resource = new URL(route.request().url()).pathname;
      const data = resource.endsWith('/addresses')
        ? []
        : resource.endsWith('/cart')
          ? { items: [], version: 1 }
          : { items: [], totalCount: 0, page: 1, pageSize: 10, unreadCount: 0, counts: {} };
      return route.fulfill({ json: data });
    });
    await page.goto(`${base}/Account/Login`);
    await submit('buyer@example.test', 'fixture-password');
    await page.waitForURL(/\/Buyer/);
    await page.waitForFunction(() => !!window.G4?.api);
    const limited = await page.evaluate(async () => {
      const response = await fetch('/api/proxy/test-payment', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ key: 'browser-key' }),
      });
      const responseInfo = { status: response.status, retry: response.headers.get('Retry-After') };
      const failures = [];
      for (let i = 0; i < 2; i++) {
        try {
          await G4.api('test-payment', 'POST', { key: 'browser-key' });
        } catch (error) {
          failures.push({ status: error.status, seconds: error.retryAfterSeconds });
          G4.message(error.message);
        }
      }
      return { responseInfo, failures };
    });
    assert.deepEqual(limited.responseInfo, { status: 429, retry: '2' });
    assert.deepEqual(limited.failures, [
      { status: 429, seconds: 2 },
      { status: 429, seconds: 2 },
    ]);
    assert.equal(paymentCalls, 2, 'client cooldown prevents a third mutation');
    assert.equal(paymentBodies[0], paymentBodies[1], 'retry retains payment key');
    await page.screenshot({ path: path.join(output, 'buyer-429-mobile.png'), fullPage: true });
    assert.deepEqual(errors, []);
    await context.close();
    console.log(
      'Rate limit browser passed: MVC/backend 429, real login form and cooldown, trusted IP forwarding, proxy Retry-After, preserved mutation key, no automatic retry, mobile layout.',
    );
  } finally {
    fs.writeFileSync(path.join(output, 'frontend.log'), logs);
    await browser?.close();
    frontend.kill();
    await new Promise((resolve) => fixture.close(resolve));
  }
}
main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
