// Opt-in integration test: real Razor, SQL QA, JWT and SignalR; simulated card/carrier only.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const { randomUUID } = require('node:crypto');
const { chromium } = require('playwright');
const connection = process.env.G4_BELL_SQL_CONNECTION;
if (!connection || !/Database=G4_NotificationBell_Test_20261027(?:;|$)/i.test(connection))
  throw new Error('Run --notification-bell-sql first and use only the dedicated bell QA database');
const output = path.resolve('artifacts/notification-bell-qa');
const build = path.resolve(process.env.G4_BELL_BUILD || 'artifacts/bell-validation/bin');
const children = [];
async function port() {
  const server = http.createServer();
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const value = server.address().port;
  await new Promise((resolve) => server.close(resolve));
  return value;
}
function launch(dll, cwd, url, env, name) {
  const log = fs.createWriteStream(path.join(output, name + '.log'));
  const child = spawn('dotnet', [dll, '--urls', url], {
    cwd,
    windowsHide: true,
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ...env },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  child.stdout.pipe(log);
  child.stderr.pipe(log);
  children.push(child);
  return child;
}
async function waitFor(url) {
  for (let attempt = 0; attempt < 150; attempt++) {
    try {
      if ((await fetch(url)).ok) return;
    } catch {
      /* startup */
    }
    await new Promise((resolve) => setTimeout(resolve, 200));
  }
  throw new Error('QA app failed to start: ' + url);
}
async function main() {
  fs.mkdirSync(output, { recursive: true });
  const apiBase = `http://127.0.0.1:${await port()}`;
  const base = `http://127.0.0.1:${await port()}`;
  let browser;
  try {
    launch(
      path.join(build, 'G4.Api/debug/G4.Api.dll'),
      path.resolve('backend/G4.Api'),
      apiBase,
      {
        ConnectionStrings__DefaultConnection: connection,
        Frontend__PublicBaseUrl: base,
        Realtime__AllowedOrigins__0: base,
        Carrier__BaseUrl: apiBase,
        Mail__Mode: 'Capture',
        Finance__WriteOff__Enabled: 'false',
        Finance__LevelPromotion__EvaluationSeconds: '3600',
      },
      'api',
    );
    launch(
      path.join(build, 'frontend/debug/frontend.dll'),
      path.resolve('frontend'),
      base,
      {
        ApiSettings__BaseUrl: apiBase,
        Realtime__PublicApiBaseUrl: apiBase,
      },
      'frontend',
    );
    await Promise.all([waitFor(apiBase + '/swagger/index.html'), waitFor(base + '/Account/Login')]);
    browser = await chromium.launch({
      headless: true,
      ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}),
    });
    const client = await browser.newContext();
    async function auth(email) {
      const response = await client.request.post(apiBase + '/api/auth/login', {
        data: { email, password: 'G4@123456' },
      });
      assert.equal(response.status(), 200, 'QA authentication');
      return response.json();
    }
    async function call(actor, route, data) {
      const response = await client.request[data === undefined ? 'get' : 'post'](
        apiBase + '/api/' + route,
        {
          headers: { Authorization: 'Bearer ' + actor.token },
          ...(data === undefined ? {} : { data }),
        },
      );
      assert.ok(response.ok(), `${route}: ${response.status()} ${await response.text()}`);
      return response.status() === 204 ? null : response.json();
    }
    const actors = {};
    for (const role of ['buyer', 'seller', 'shipper', 'admin'])
      actors[role] = await auth(role + '@example.test');
    const other = await auth('bell-other@example.test');
    const sessions = {},
      errors = [],
      frames = [];
    for (const role of Object.keys(actors)) {
      const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
      const page = await context.newPage();
      page.on('pageerror', (error) => errors.push(role + ': ' + error.message));
      page.on('websocket', (socket) =>
        socket.on('framereceived', (event) => {
          if (String(event.payload).includes('notification-inbox')) frames.push(role);
        }),
      );
      await page.goto(base + '/Account/Login');
      await page.locator('#email').fill(role + '@example.test');
      await page.locator('#password').fill('G4@123456');
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
      await page.waitForFunction(() => window.G4?.Realtime?.connected === true);
      await page.waitForFunction(() =>
        document
          .getElementById('notification-bell-count')
          .textContent.includes('thông báo chưa đọc'),
      );
      assert.equal(await page.locator('#notification-bell-toggle').count(), 1);
      sessions[role] = { context, page };
    }
    async function inbox(role) {
      return call(actors[role], 'me/notifications?pageSize=30');
    }
    async function sync(role) {
      const state = await inbox(role);
      await sessions[role].page.waitForFunction((unread) => {
        const badge = document.getElementById('notification-bell-badge');
        return unread === 0
          ? badge.hidden
          : !badge.hidden && badge.textContent === (unread > 9 ? '9+' : String(unread));
      }, state.unreadCount);
      return state;
    }
    const address = (await call(actors.buyer, 'addresses'))[0];
    const products = await call(actors.buyer, 'catalog/random?count=5');
    // Legacy random endpoint returns five products from the seed seller, without sellerId.
    const product = products[0];
    assert.ok(product, 'Seed product available');
    async function createOrder() {
      return call(actors.buyer, 'orders', {
        addressId: address.id,
        items: [{ productId: product.productId, quantity: 1 }],
        checkoutKey: randomUUID(),
      });
    }
    const untouched = await call(other, 'me/notifications');
    const order = await createOrder();
    await call(actors.buyer, `orders/${order.id}/pay/card`, {
      number: '4111111111111111',
      expiry: '12/30',
      key: randomUUID(),
    });
    await call(actors.seller, `seller/orders/${order.id}/prepare`, {});
    const shipment = await call(actors.seller, `seller/orders/${order.id}/ship`, {});
    await sessions.shipper.page.waitForFunction(
      () => !document.getElementById('notification-bell-badge').hidden,
    );
    const shipperList = await inbox('shipper');
    assert.ok(
      shipperList.items.some(
        (n) => n.title === 'Có vận đơn chờ nhận' && !n.message.includes(`#${order.id}`),
      ),
      'Shipper job alert is generic',
    );
    await call(actors.shipper, `shipper/shipments/${shipment.id}/claim`, {});
    await call(actors.shipper, `shipments/${shipment.id}/events`, {
      status: 'PickedUp',
      eventId: randomUUID(),
      location: 'Bell QA',
    });
    // The badge must update while a business dialog contains an unsaved draft.
    const buyerPage = sessions.buyer.page;
    await buyerPage.evaluate(() => {
      document.getElementById('action-fields').innerHTML = '<input id="bell-qa-draft" />';
      document.getElementById('action-dialog').showModal();
    });
    await buyerPage.locator('#bell-qa-draft').fill('Giữ nội dung đang nhập');
    const before = (await inbox('buyer')).unreadCount;
    for (let i = 0; i < 10; i++) await createOrder();
    await buyerPage.waitForFunction(
      () => document.getElementById('notification-bell-badge').textContent === '9+',
    );
    assert.equal(await buyerPage.locator('#bell-qa-draft').inputValue(), 'Giữ nội dung đang nhập');
    assert.ok((await inbox('buyer')).unreadCount > before);
    await buyerPage.evaluate(() => document.getElementById('action-dialog').close());
    const afterOther = await call(other, 'me/notifications');
    assert.equal(
      afterOther.totalCount,
      untouched.totalCount,
      'Another buyer received private notifications',
    );
    const privateItem = (await inbox('buyer')).items[0];
    const forbidden = await client.request.post(
      apiBase + `/api/me/notifications/${privateItem.id}/read`,
      {
        headers: { Authorization: 'Bearer ' + other.token },
        data: {},
      },
    );
    assert.equal(forbidden.status(), 404);
    const anonymous = await client.request.get(apiBase + '/api/me/notifications');
    assert.equal(anonymous.status(), 401);
    for (const role of Object.keys(actors)) {
      await sync(role);
      const page = sessions[role].page;
      await page.locator('#notification-bell-toggle').click();
      await page.waitForFunction(
        () =>
          document.getElementById('notification-bell-list').getAttribute('aria-busy') === 'false',
      );
      const unread = (await inbox(role)).unreadCount;
      await page.keyboard.press('Escape');
      assert.equal(
        await page.locator('#notification-bell-toggle').getAttribute('aria-expanded'),
        'false',
      );
      assert.equal(
        await page.evaluate(() => document.activeElement.id),
        'notification-bell-toggle',
      );
      assert.equal((await inbox(role)).unreadCount, unread, 'Opening bell marked items read');
      await page.locator('#notification-bell-toggle').click();
      for (const width of [1440, 390, 320]) {
        await page.setViewportSize({ width, height: 900 });
        const rect = await page.locator('#notification-bell-panel').boundingBox();
        assert.ok(
          rect.x >= 0 && rect.x + rect.width <= width + 1,
          `${role} panel overflows ${width}`,
        );
        assert.ok(
          await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1),
          `${role} horizontal page overflow ${width}`,
        );
        await page.screenshot({ path: path.join(output, `${role}-${width}.png`) });
      }
      await page.setViewportSize({ width: 1440, height: 1000 });
      await page.keyboard.press('Escape');
    }
    // A second tab must see mark-all over the same SignalR connection group.
    const second = await sessions.buyer.context.newPage();
    await second.goto(base + '/Buyer');
    await second.waitForFunction(() => window.G4?.Realtime?.connected === true);
    await buyerPage.locator('#notification-bell-toggle').click();
    await buyerPage.locator('#notification-bell-next').click();
    await buyerPage.waitForFunction(() =>
      document.getElementById('notification-bell-page').textContent.startsWith('Trang 2'),
    );
    await buyerPage.locator('#notification-bell-read-all').click();
    await second.waitForFunction(() => document.getElementById('notification-bell-badge').hidden);
    await buyerPage.reload();
    await buyerPage.waitForFunction(
      () =>
        document.getElementById('notification-bell-count').textContent ===
        'Không có thông báo chưa đọc',
    );
    // Recover a missed event after offline/online without a page reload.
    await sessions.buyer.context.setOffline(true);
    await createOrder();
    await sessions.buyer.context.setOffline(false);
    await buyerPage.waitForFunction(
      () => !document.getElementById('notification-bell-badge').hidden,
    );
    await buyerPage.locator('#notification-bell-toggle').click();
    await buyerPage.waitForFunction(
      () => !!document.querySelector('#notification-bell-list .is-unread'),
    );
    const selected = await buyerPage
      .locator('#notification-bell-list button')
      .first()
      .getAttribute('data-notification-id');
    await buyerPage.locator('#notification-bell-list button').first().click();
    await buyerPage.waitForURL(/\/Buyer\?orderId=\d+#orders/);
    await buyerPage.waitForFunction(() => document.getElementById('detail-dialog').open);
    assert.ok(
      (await inbox('buyer')).items.find((n) => String(n.id) === selected).readAt,
      'Click did not persist read state',
    );
    for (const role of ['seller', 'shipper', 'admin']) {
      const page = sessions[role].page;
      await page.locator('#notification-bell-toggle').click();
      await page.locator('#notification-bell-list button').first().click();
      if (role === 'admin') await page.waitForURL(/\/Admin#seller-debts/);
      else {
        await page.waitForURL(
          new RegExp(`/${role === 'seller' ? 'Seller' : 'Shipper'}\\?orderId=\\d+`),
        );
        await page.waitForFunction(() => document.getElementById('detail-dialog').open);
      }
    }
    // Exercise transient read errors and HTML escaping without modifying persisted QA content.
    await second.route('**/api/proxy/me/notifications?*', (route) =>
      route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Tạm thời mất kết nối' }),
      }),
    );
    await second.locator('#notification-bell-toggle').click();
    await second.waitForFunction(() => !document.getElementById('notification-bell-error').hidden);
    await second.unroute('**/api/proxy/me/notifications?*');
    await second.route('**/api/proxy/me/notifications?*', (route) =>
      route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({
          page: 1,
          pageSize: 10,
          totalCount: 1,
          unreadCount: 1,
          latestId: 1,
          items: [
            {
              id: 1,
              title: '<img src=x onerror=alert(1)>',
              message: '<script>alert(1)</script>',
              targetUrl: '/Buyer#orders',
              createdAt: '2026-10-27T12:00:00Z',
              readAt: null,
            },
          ],
        }),
      }),
    );
    await second.locator('#notification-bell-refresh').click();
    await second.waitForFunction(() => document.getElementById('notification-bell-error').hidden);
    assert.equal(
      await second.locator('#notification-bell-list img, #notification-bell-list script').count(),
      0,
    );
    assert.ok((await second.locator('#notification-bell-list').textContent()).includes('<img'));
    assert.ok(
      Object.keys(actors).every((role) => frames.includes(role)),
      'Expected real SignalR notification frames for every role',
    );
    assert.deepEqual(errors, []);
    console.log(
      'Bell Chrome/SQL/SignalR passed: four roles, 320/390/1440px, private scope, 9+, keyboard, paging, draft, read/link, multi-tab and offline recovery',
    );
  } finally {
    await browser?.close();
    for (const child of children) child.kill();
  }
}
main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
