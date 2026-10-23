// Real Razor pages with isolated read-only API fixtures. Build frontend first.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const { chromium } = require('playwright');

const output = path.resolve('artifacts/countdown-ui');
let started = Date.now();
const at = (seconds) => new Date(started + seconds * 1000).toISOString();
const paged = (items = []) => ({
  page: 1,
  pageSize: 10,
  totalCount: items.length,
  items,
  counts: { all: items.length, approval: 0, receipt: 1 },
  openCount: items.length,
});
const debt = () => ({
  sellerId: 2,
  balance: 10,
  cycleNumber: 1,
  startedAt: at(0),
  warningAt: at(45),
  dueAt: at(60),
  earliestWriteOffAt: at(300),
  ageSeconds: 0,
  overdue: false,
  suspended: false,
  accountStatus: 'Active',
});
const funds = () => [
  {
    orderId: 31,
    processing: 87.7,
    onHold: 0,
    sellerLevel: 1,
    holdPolicyVersion: 1,
    holdSeconds: 300,
    holdStartedAt: at(0),
    releaseAt: at(300),
    currency: 'USD',
  },
  {
    orderId: 32,
    processing: 45,
    onHold: 0,
    sellerLevel: 2,
    holdPolicyVersion: 1,
    holdSeconds: 180,
    holdStartedAt: null,
    releaseAt: null,
    currency: 'USD',
  },
  {
    orderId: 33,
    processing: 5,
    onHold: 25,
    sellerLevel: 1,
    holdPolicyVersion: 1,
    holdSeconds: 30,
    holdStartedAt: at(-40),
    releaseAt: at(-10),
    currency: 'USD',
    blockReason: 'Dispute',
    disputeId: 3,
  },
];
const address = () => ({
  id: 40,
  status: 'Pending',
  oldAddress: 'Địa chỉ cũ',
  newAddress: 'Địa chỉ mới',
  reason: 'Buyer chuyển nơi nhận',
  createdAt: at(0),
  expiresAt: at(120),
  oldShippingCost: 2,
  newShippingCost: 2,
  buyerRefund: 0,
});
const cases = () =>
  [
    { id: 3, orderId: 33, status: 'AwaitingSeller', responseDueAt: at(45) },
    { id: 4, orderId: 2, status: 'AwaitingBuyer', responseDueAt: at(45) },
    {
      id: 5,
      orderId: 3,
      status: 'ExecutingAgreement',
      proposal: 'ReturnRefund',
      outcome: 'AgreedReturn',
      returnConfirmationDueAt: at(45),
    },
  ].map((item) => ({
    ...item,
    buyerId: 1,
    sellerId: 2,
    isOpen: true,
    description: 'Sản phẩm cần được kiểm tra',
    createdAt: at(0),
    updatedAt: at(0),
    totalPrice: 100,
    heldAmount: 25,
  }));
const order = (id) => ({
  id,
  status: id === 1 ? 'AwaitingPayment' : 'Delivered',
  orderDate: at(0),
  updatedAt: at(0),
  paymentExpiresAt: at(120),
  totalPrice: 100,
  shippingFee: 2,
  subtotal: 98,
  addressSnapshot: '123 Đường kiểm thử, Hà Nội',
  totalWeightKg: 1,
  promotions: [],
  items: [{ productId: 1, productTitleSnapshot: 'Sản phẩm demo', quantity: 1, unitPrice: 98 }],
  payments: id === 1 ? [] : [{ status: 'Succeeded', amount: 100, method: 'CreditCard' }],
  refunds: [],
  events: [],
  shipments: [{ id: 1, direction: 'Outbound', status: 'Delivered' }],
  returnWindowClosesAt: at(300),
  canRequestReturn: true,
  canOpenDispute: true,
  addressChanges: id === 4 ? [address()] : [],
  returnRequest:
    id === 3
      ? { id: 100, status: 'ReturnShipping', confirmationDueAt: at(45), reason: 'Sai hàng' }
      : null,
  sellerFunds: funds().find((item) => item.orderId === id),
});
const level = () => ({
  sellerId: 2,
  level: 1,
  history: [],
  lastEvaluatedAt: at(0),
  nextEvaluationAt: at(60),
  preview: {
    previousLevel: 1,
    resultLevel: 1,
    reason: 'Giữ cấp hiện tại',
    metrics: {
      windowStart: at(-90 * 86400),
      windowEnd: at(0),
      paidOrders: 0,
      feedbackCount: 0,
      deliveryCount: 0,
    },
    policy: {},
    tiers: [],
  },
});

function response(resource) {
  if (resource === 'cart') return { version: 'fixture', items: [] };
  if (resource === 'addresses') return [];
  if (resource === 'orders/page')
    return paged(
      [1, 2, 3, 4].map((id) => ({
        id,
        status: id === 1 ? 'AwaitingPayment' : 'Delivered',
        paymentExpiresAt: at(120),
        orderDate: at(0),
        updatedAt: at(0),
        totalPrice: 100,
        productTitle: 'Sản phẩm demo',
        itemCount: 1,
        productCount: 1,
        sellerName: 'Shop demo',
        buyerName: 'Buyer demo',
      })),
    );
  if (/^orders\/\d+$/.test(resource)) return order(Number(resource.split('/')[1]));
  if (resource === 'seller/finance')
    return {
      level: 1,
      holdSeconds: 300,
      processing: 137.7,
      available: 0,
      onHold: 25,
      negative: 10,
      payoutPending: 0,
      paidOut: 0,
      monthlySalesLimit: 5000,
      monthlySalesAmount: 200,
      levelProgress: {},
    };
  if (resource === 'seller/finance/funds/page') return paged(funds());
  if (resource === 'seller/debt' || resource === 'admin/seller-debts/2') return debt();
  if (resource === 'seller/level' || resource === 'admin/seller-levels/2') return level();
  if (resource === 'admin/seller-levels')
    return { ...paged([{ sellerId: 2, level: 1, sellerName: 'Seller demo' }]), policy: {} };
  if (resource === 'admin/seller-debts')
    return paged([
      {
        sellerId: 2,
        email: 'seller@example.test',
        negativeBalance: 10,
        debtDueAt: at(60),
        debtStartedAt: at(0),
        status: 'Active',
      },
    ]);
  if (resource === 'disputes/page') return paged(cases());
  if (/^disputes\/\d+$/.test(resource))
    return {
      case: cases().find((item) => item.id === Number(resource.split('/')[1])),
      entries: [],
    };
  if (resource === 'seller/returns/page')
    return paged([
      {
        id: 100,
        orderId: 3,
        status: 'ReturnShipping',
        shipmentStatus: 'Delivered',
        confirmationDueAt: at(45),
        reason: 'Sai hàng',
        disputed: false,
      },
    ]);
  return paged();
}

async function listen(server) {
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  return server.address().port;
}

async function main() {
  fs.mkdirSync(output, { recursive: true });
  const api = http.createServer(async (req, res) => {
    res.setHeader('Content-Type', 'application/json');
    if (req.url === '/api/clock') return res.end('{"ok":true}');
    if (req.url !== '/api/auth/login') return res.writeHead(404).end();
    let body = '';
    for await (const chunk of req) body += chunk;
    res.end(
      JSON.stringify({
        token: 'countdown-fixture',
        userId: 1,
        username: 'Demo',
        role: JSON.parse(body).email.split('@')[0],
        expiresAt: '2099-01-01T00:00:00Z',
      }),
    );
  });
  const apiPort = await listen(api),
    reservation = http.createServer();
  const port = await listen(reservation);
  await new Promise((resolve) => reservation.close(resolve));
  const base = `http://127.0.0.1:${port}`;
  const frontend = spawn(
    'dotnet',
    [
      path.resolve(process.env.FRONTEND_DLL || 'frontend/bin/Debug/net8.0/frontend.dll'),
      '--urls',
      base,
    ],
    {
      cwd: path.resolve('frontend'),
      windowsHide: true,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Development',
        ApiSettings__BaseUrl: `http://127.0.0.1:${apiPort}`,
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    },
  );
  let logs = '',
    browser;
  frontend.stdout.on('data', (value) => (logs += value));
  frontend.stderr.on('data', (value) => (logs += value));
  try {
    for (let attempt = 0; ; attempt++) {
      try {
        if ((await fetch(`${base}/Account/Login`)).status === 200) break;
      } catch {}
      if (attempt >= 100) throw Error(logs);
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    browser = await chromium.launch({
      headless: true,
      executablePath:
        process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe',
    });
    for (const width of [320, 390, 1280]) {
      for (const role of ['buyer', 'seller', 'admin', 'shipper']) {
        started = Date.now();
        const context = await browser.newContext({ viewport: { width, height: 900 } });
        await context.addInitScript(() => {
          const original = Date.now;
          Date.now = () => original() + 9 * 3600000;
        });
        const page = await context.newPage(),
          errors = [],
          writes = [];
        page.setDefaultTimeout(10000);
        page.on('pageerror', (error) => errors.push(error.message));
        await page.route('**/Realtime/Session', (route) =>
          route.fulfill({ status: 503, body: '{}' }),
        );
        await page.route('**/api/proxy/**', (route) => {
          if (route.request().method() !== 'GET') writes.push(route.request().url());
          const resource = new URL(route.request().url()).pathname.replace('/api/proxy/', '');
          return route.fulfill({
            json: response(resource),
            headers: { 'X-Server-Time': new Date().toISOString() },
          });
        });
        await page.goto(`${base}/Account/Login`);
        await page.locator('#email').fill(`${role}@countdown.test`);
        await page.locator('#password').fill('fixture-password');
        await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
        await page.waitForURL(new RegExp(`/${role}`, 'i'));
        await page.waitForFunction(() => window.G4?.Countdown);
        const select = async (id) => {
          const button = page.locator(`[data-tab="${id}"]`);
          if (!(await button.isVisible())) await page.locator('#navigation-toggle').click();
          await button.click();
        };
        const close = () => page.locator('#detail-close').click();
        if (role === 'buyer') {
          await select('orders');
          const timer = page.locator('[data-countdown-kind="payment"] [data-countdown-value]');
          await timer.waitFor();
          const before = await timer.textContent();
          await page.waitForTimeout(1100);
          assert.notEqual(await timer.textContent(), before);
          const deadline = await page
            .locator('[data-countdown-kind="payment"]')
            .getAttribute('data-countdown');
          await page.reload();
          await timer.waitFor();
          assert.equal(
            await page.locator('[data-countdown-kind="payment"]').getAttribute('data-countdown'),
            deadline,
          );
          assert.ok((await page.evaluate(() => Date.now() - G4.Countdown.now())) > 8 * 3600000);
          await page.locator('[data-open-order="2"]').click();
          await page.locator('[data-countdown-kind="return-window"]').waitFor();
          await close();
          await page.locator('[data-open-order="4"]').click();
          await page.locator('[data-countdown-kind="address"]').waitFor();
          await close();
          await page.locator('[data-open-order="3"]').click();
          await page.locator('[data-countdown-kind="receipt"]').waitFor();
        } else if (role === 'seller') {
          await select('finance');
          await page.locator('[data-fund-order="31"]').waitFor();
          assert.equal(await page.locator('[data-fund-order]').count(), 3);
          assert.match(
            await page.locator('[data-fund-order="32"]').innerText(),
            /Chưa bắt đầu đếm/,
          );
          assert.match(
            await page.locator('[data-fund-order="33"]').innerText(),
            /Đã đủ thời gian giữ/,
          );
          assert.match(await page.locator('[data-fund-order="33"]').innerText(), /\$25.00/);
          await page
            .locator('#seller-funds-list')
            .screenshot({ path: path.join(output, `funds-${width}.png`) });
          await select('seller-levels');
          await page.locator('[data-countdown-kind="level"]').waitFor();
          await select('seller-returns');
          await page.locator('#seller-return-list [data-countdown-kind="receipt"]').waitFor();
          await select('disputes');
          await page.locator('[data-countdown-kind="dispute"]').first().waitFor();
          await page.locator('[data-open-case="5"]').click();
          await page.locator('#detail-content [data-countdown-kind="dispute"]').waitFor();
        } else if (role === 'admin') {
          await select('seller-debts');
          await page.locator('[data-debt-seller="2"]').click();
          await page.locator('#admin-debt-summary [data-countdown-kind="debt"]').first().waitFor();
          assert.equal(await page.locator('#admin-debt-summary [data-countdown]').count(), 3);
          await select('seller-levels');
          await page.locator('[data-level-seller="2"]').click();
          await page.locator('[data-countdown-kind="level"]').waitFor();
        } else {
          await page.evaluate((request) => {
            const host = document.createElement('section');
            host.innerHTML = G4.AddressChanges.shipperNotice(
              { id: 7, direction: 'Outbound' },
              { addressChanges: [{ ...request, status: 'AwaitingShipper' }] },
            );
            document.querySelector('main').append(host);
          }, address());
          await page.locator('[data-countdown-kind="address"]').waitFor();
        }
        const overflow = await page
          .locator('[data-countdown]')
          .evaluateAll(
            (nodes) =>
              nodes.filter(
                (node) =>
                  node.getBoundingClientRect().width &&
                  (node.scrollWidth > node.clientWidth + 1 ||
                    node.getBoundingClientRect().right > window.innerWidth + 1),
              ).length,
          );
        assert.equal(overflow, 0, `${role} countdown overflows at ${width}px`);
        const timers = page.locator('[data-countdown-value]');
        assert.ok((await timers.count()) > 0);
        assert.equal(await timers.first().getAttribute('aria-live'), 'off');
        await page.screenshot({ path: path.join(output, `${role}-${width}.png`), fullPage: true });
        assert.deepEqual(errors, [], `${role} browser errors`);
        assert.deepEqual(writes, [], `${role} countdown must not submit mutations`);
        const clock = await page.request.get(`${base}/api/proxy/clock`);
        assert.ok(clock.headers()['x-server-time'], 'proxy forwards backend clock');
        await context.close();
      }
    }
    console.log(
      'Countdown browser QA: four roles, mobile/desktop, clock skew, reload, per-order holds and no writes passed.',
    );
  } finally {
    await browser?.close();
    frontend.kill();
    await new Promise((resolve) => api.close(resolve));
  }
}
main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
