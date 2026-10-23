// Opt-in only: QA frontend/API/database; PayPal is a local HTTP contract stub, never a real charge.
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const base = process.env.G4_REFRESH_E2E_URL,
  apiBase = 'http://127.0.0.1:5255/api',
  stubBase = 'http://127.0.0.1:5265';
if (base !== 'http://127.0.0.1:5135')
  throw new Error('Use the dedicated QA frontend at http://127.0.0.1:5135');
const folder = 'artifacts/commerce-refresh-qa';
const orders = new Map(),
  refundRequests = new Map();
let captures = 0,
  labels = 0,
  lostReply = false;
const provider = http.createServer(async (req, res) => {
  try {
    let raw = '';
    for await (const part of req) raw += part;
    const body = raw && req.headers['content-type']?.includes('json') ? JSON.parse(raw) : {};
    const url = new URL(req.url, stubBase),
      path = url.pathname;
    const json = (code, data) => {
      res.writeHead(code, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(data));
    };
    if (path === '/v1/oauth2/token')
      return json(200, { access_token: 'qa-local-paypal-token', token_type: 'Bearer' });
    if (path === '/v2/checkout/orders' && req.method === 'POST') {
      const key = req.headers['paypal-request-id'];
      let order = [...orders.values()].find((o) => o.key === key);
      if (!order) {
        const id = 'PP-QA-' + Date.now() + '-' + (orders.size + 1);
        order = {
          id,
          key,
          status: 'CREATED',
          units: body.purchase_units,
          context: body.payment_source.paypal.experience_context,
        };
        orders.set(id, order);
      }
      return json(201, {
        id: order.id,
        status: order.status,
        links: [{ rel: 'payer-action', href: stubBase + '/approve/' + order.id }],
      });
    }
    const approval = path.match(/^\/approve\/([^/]+)$/);
    if (approval) {
      const order = orders.get(approval[1]);
      if (!order) return json(404, {});
      if (req.method === 'POST') {
        order.status = 'APPROVED';
        res.writeHead(302, { Location: order.context.return_url + '&token=' + order.id });
        return res.end();
      }
      res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
      return res.end(
        `<html lang="vi"><h1>PayPal HTTP stub · QA</h1><p>${order.units.length} đơn trong một lần xác nhận</p><form method="post"><button>Xác nhận cả giỏ</button></form></html>`,
      );
    }
    const capture = path.match(/^\/v2\/checkout\/orders\/([^/]+)(\/capture)?$/);
    if (capture) {
      const order = orders.get(capture[1]);
      if (!order) return json(404, {});
      if (capture[2]) {
        captures++;
        order.status = 'COMPLETED';
      }
      const result = {
        id: order.id,
        status: order.status,
        purchase_units: order.units.map((u, i) => ({
          ...u,
          ...(order.status === 'COMPLETED'
            ? {
                payments: {
                  captures: [{ id: order.id + '-CAP-' + i, status: 'COMPLETED', amount: u.amount }],
                },
              }
            : {}),
        })),
      };
      if (capture[2] && !lostReply) {
        lostReply = true;
        return json(503, { error: 'QA capture succeeded, reply lost' });
      }
      return json(200, result);
    }
    const refund = path.match(/^\/v2\/payments\/captures\/([^/]+)\/refund$/);
    if (refund) {
      const key = req.headers['paypal-request-id'];
      if (!refundRequests.has(key))
        refundRequests.set(key, {
          id: 'REF-QA-' + (refundRequests.size + 1),
          captureId: refund[1],
          amount: body.amount,
        });
      return json(201, { id: refundRequests.get(key).id, status: 'COMPLETED' });
    }
    return json(404, { error: 'Unexpected QA provider route' });
  } catch (e) {
    res.writeHead(500);
    res.end(JSON.stringify({ error: e.message }));
  }
});
(async () => {
  await fs.mkdir(folder, { recursive: true });
  await new Promise((resolve) => provider.listen(5265, '127.0.0.1', resolve));
  let browser;
  try {
    browser = await chromium.launch({ headless: true });
  } catch {
    browser = await chromium.launch({ headless: true, channel: 'msedge' });
  }
  const errors = [];
  try {
    const request = await browser.newContext();
    async function auth(email) {
      const r = await request.request.post(apiBase + '/auth/login', {
        data: { email, password: 'G4@123456' },
      });
      assert.equal(r.status(), 200, 'QA login ' + email);
      return r.json();
    }
    const buyerAuth = await auth('buyer@example.test'),
      sellerAuth = await auth('seller@example.test'),
      seller2Auth = await auth('seller2@example.test'),
      shipperAuth = await auth('shipper@example.test');
    async function call(actor, route, data) {
      const method = data === undefined ? 'get' : 'post';
      const r = await request.request[method](apiBase + '/' + route, {
        headers: { Authorization: 'Bearer ' + actor.token },
        ...(data === undefined ? {} : { data }),
      });
      assert.ok(r.ok(), `${method} ${route}: ${r.status()} ${await r.text()}`);
      return r.json();
    }
    async function pageFor(email) {
      const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
      const p = await ctx.newPage();
      p.on('pageerror', (e) => errors.push(e.message));
      await p.goto(base + '/Account/Login');
      await p.getByLabel('Địa chỉ thư điện tử', { exact: true }).fill(email);
      await p.getByLabel('Mật khẩu', { exact: true }).fill('G4@123456');
      await p.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
      await p.locator('#realtime-status[data-connected="true"]').waitFor();
      return { ctx, p };
    }
    const buyer = await pageFor('buyer@example.test'),
      seller = await pageFor('seller@example.test');
    const isolated = await pageFor('isolated-refresh@example.test');
    await isolated.p.evaluate(() => {
      window.privateUpdates = [];
      G4.Realtime.connection.on('CommerceChanged', (event) => window.privateUpdates.push(event));
    });
    await seller.p.getByRole('button', { name: /Trả hàng \/ Hoàn tiền/ }).click();
    await buyer.p.locator('#count').selectOption('3');
    await buyer.p.locator('#random').click();
    await buyer.p.locator('#continue:enabled').waitFor();
    await buyer.p.locator('#continue').click();
    await buyer.p.locator('#pay:enabled').waitFor();
    await buyer.p.locator('#pay').click();
    await buyer.p.waitForURL(stubBase + '/approve/**');
    assert.equal(orders.size, 1);
    const providerOrder = [...orders.values()][0];
    assert.equal(providerOrder.units.length, 2);
    await buyer.p.getByRole('button', { name: 'Xác nhận cả giỏ', exact: true }).click();
    await buyer.p.waitForURL(base + '/Buyer?paypalCheckoutId=**');
    await buyer.p.waitForFunction(
      () =>
        document.getElementById('paypal-cart-result').innerText.includes('Đã thanh toán') &&
        !document.getElementById('paypal-cart-result').innerText.includes('Đang xác minh'),
      null,
      { timeout: 30000 },
    );
    const checkoutId = Number(new URL(buyer.p.url()).searchParams.get('paypalCheckoutId'));
    const receipt = await call(buyerAuth, `buyer/paypal-checkouts/${checkoutId}`);
    assert.equal(receipt.status, 'Succeeded');
    assert.equal(receipt.orders.length, 2);
    assert.ok(receipt.orders.every((o) => o.paymentStatus === 'Succeeded'));
    assert.equal(captures, 1, 'Lost capture response is queried, never captured again by worker');
    await buyer.p.screenshot({ path: folder + '/paypal-whole-cart.jpg', fullPage: true });
    console.log(
      'E2E: one PayPal confirmation, two captures, lost reply reconciled and both orders paid.',
    );
    const ownOrders = await call(sellerAuth, 'orders');
    const orderId = receipt.orders.find((o) => ownOrders.some((s) => s.id === o.id)).id;
    const otherOrderId = receipt.orders.find((o) => o.id !== orderId).id;
    await call(sellerAuth, `seller/orders/${orderId}/prepare`, {});
    await seller.p.getByRole('button', { name: 'Đơn hàng', exact: true }).click();
    await seller.p.locator('#order-list [data-open-order="' + orderId + '"]').click();
    await seller.p.locator('[data-action="fail-carrier"]').click();
    await seller.p.locator('#action-confirm').click();
    await seller.p.locator('#message').filter({ hasText: 'Tạo vận đơn thất bại' }).waitFor();
    const failed = (await call(sellerAuth, `orders/${orderId}`)).shipments.find(
      (s) => s.direction === 'Outbound',
    );
    assert.equal(failed.status, 'ShipmentCreationFailed');
    assert.equal(failed.trackingNumber, null);
    await seller.p.locator('[data-action="ship"]').click();
    await seller.p.locator('#action-confirm').click();
    await seller.p.waitForFunction(() => !document.querySelector('[data-action="ship"]'));
    const outbound = (await call(sellerAuth, `orders/${orderId}`)).shipments.find(
      (s) => s.direction === 'Outbound',
    );
    assert.equal(outbound.id, failed.id);
    await call(shipperAuth, `shipper/shipments/${outbound.id}/claim`, {});
    for (const status of ['PickedUp', 'InTransit', 'OutForDelivery', 'Delivered'])
      await call(shipperAuth, `shipments/${outbound.id}/events`, {
        status,
        eventId: 'e2e-' + status + '-' + Date.now(),
      });
    // Open through the real order-list event handler after the live list refresh arrives.
    await buyer.p.locator(`[data-open-order="${orderId}"]`).first().click();
    await buyer.p.locator('[data-action="review-seller"]').click();
    await buyer.p.setViewportSize({ width: 390, height: 844 });
    await buyer.p.locator('#seller-feedback-rating').selectOption('4');
    await buyer.p.locator('#seller-feedback-comment').fill('Giữ nguyên nội dung đang nhập');
    await call(seller2Auth, `seller/orders/${otherOrderId}/prepare`, {});
    await buyer.p.waitForTimeout(900);
    assert.equal(
      await buyer.p.locator('#seller-feedback-comment').inputValue(),
      'Giữ nguyên nội dung đang nhập',
    );
    const bounds = await buyer.p.locator('#seller-feedback-dialog').boundingBox();
    assert.ok(bounds.width <= 366 && bounds.width > 250);
    assert.ok(
      await buyer.p
        .locator('#seller-feedback-comment')
        .evaluate(
          (el) =>
            el.getBoundingClientRect().right <=
            el.closest('dialog').getBoundingClientRect().right - 8,
        ),
    );
    await buyer.p
      .locator('#seller-feedback-dialog')
      .screenshot({ path: folder + '/buyer-feedback-mobile.jpg' });
    await buyer.p.locator('#seller-feedback-submit').click();
    await buyer.p.locator('#seller-feedback-dialog').waitFor({ state: 'hidden' });
    await buyer.p.setViewportSize({ width: 1280, height: 900 });
    await seller.p.locator('#detail-close').click();
    await seller.p.getByRole('button', { name: /Trả hàng \/ Hoàn tiền/ }).click();
    await buyer.p.locator('[data-action="return"]').click();
    await buyer.p.locator('#return-reason').selectOption('ChangedMind');
    await buyer.p.locator('#return-confirm:enabled').waitFor();
    await buyer.p.locator('#return-confirm').click();
    await buyer.p.locator('#return-dialog').waitFor({ state: 'hidden' });
    const row = seller.p
      .locator('#seller-return-list article')
      .filter({ hasText: new RegExp('Đơn #' + orderId + '\\b') });
    await row.waitFor({ timeout: 15000 }); // No refresh click: SignalR invalidates seller's approval inbox.
    await seller.p.waitForFunction(() =>
      /[1-9]\d* chờ duyệt/.test(document.getElementById('seller-return-summary').innerText),
    );
    await seller.p.screenshot({ path: folder + '/seller-return-inbox.jpg', fullPage: true });
    await row.getByRole('button', { name: 'Xem đơn và xử lý', exact: true }).click();
    await seller.p.locator('[data-action="approve-return-failure"]').click();
    await seller.p.locator('#action-confirm').click();
    await seller.p.locator('#message').filter({ hasText: 'Tạo vận đơn thất bại' }).waitFor();
    const retFailed = await call(sellerAuth, `orders/${orderId}`);
    const returnLabel = retFailed.shipments.find((s) => s.direction === 'Return');
    assert.equal(returnLabel.status, 'ShipmentCreationFailed');
    assert.equal(retFailed.returnRequest.shippingCostChargedAt, null);
    await seller.p.locator('[data-action="retry-return"]').click();
    await seller.p.locator('#action-confirm').click();
    await seller.p.waitForFunction(() => !document.querySelector('[data-action="retry-return"]'));
    const ret = await call(sellerAuth, `orders/${orderId}`);
    assert.equal(ret.shipments.find((s) => s.direction === 'Return').id, returnLabel.id);
    await call(shipperAuth, `shipper/shipments/${returnLabel.id}/claim`, {});
    for (const status of ['PickedUp', 'InTransit', 'OutForDelivery', 'Delivered'])
      await call(shipperAuth, `shipments/${returnLabel.id}/events`, {
        status,
        eventId: 'return-e2e-' + status + '-' + Date.now(),
      });
    await seller.p.locator('[data-action="receive-return"]').waitFor({ timeout: 15000 });
    await seller.p.locator('[data-action="receive-return"]').click();
    await seller.p.locator('#action-confirm').click();
    await seller.p.waitForFunction(() => !document.querySelector('[data-action="receive-return"]'));
    const done = await call(buyerAuth, `orders/${orderId}`);
    const refunded = done.refunds.filter((r) => r.status === 'Succeeded' && r.reason === 'return');
    assert.equal(refunded.length, 1);
    assert.equal(refunded[0].amount, done.returnRequest.buyerRefundAmount);
    assert.equal(refundRequests.size, 1);
    const refund = [...refundRequests.values()][0];
    const unitIndex = providerOrder.units.findIndex((u) => u.reference_id === 'order-' + orderId);
    assert.equal(refund.captureId, providerOrder.id + '-CAP-' + unitIndex);
    const entries = await call(sellerAuth, 'seller/finance/transactions/page?pageSize=50');
    assert.equal(
      entries.items.filter((t) => t.orderId === orderId && t.type === 'ReturnShippingCost').length,
      1,
    );
    console.log(
      'E2E: compact rating modal preserves input; carrier failure/retry in both directions; live seller approval inbox; one fee and refund to correct PayPal unit.',
    );
    await buyer.p.evaluate(() => G4.closeDetail());
    await buyer.ctx.setOffline(true);
    await buyer.p.locator('#realtime-status[data-connected="false"]').waitFor({ timeout: 15000 });
    await call(seller2Auth, `seller/orders/${otherOrderId}/ship`, {});
    await buyer.ctx.setOffline(false);
    await buyer.p.locator('#realtime-status[data-connected="true"]').waitFor({ timeout: 30000 });
    await buyer.p.waitForFunction(
      (id) => document.querySelector(`[data-order-row="${id}"]`)?.innerText.includes('Đang giao'),
      otherOrderId,
      { timeout: 15000 },
    );
    const cannotJoin = await buyer.p.evaluate(async () => {
      try {
        await G4.Realtime.connection.invoke('JoinGroup', 'role:admin');
        return false;
      } catch {
        return true;
      }
    });
    assert.equal(cannotJoin, true);
    assert.deepEqual(
      await isolated.p.evaluate(() => window.privateUpdates),
      [],
      'Unrelated buyer must not receive private order updates',
    );
    const anonymous = await request.request.post(
      'http://127.0.0.1:5255/hubs/commerce/negotiate?negotiateVersion=1',
    );
    assert.equal(anonymous.status(), 401);
    assert.deepEqual(errors, []);
    console.log(
      'E2E passed: whole-cart PayPal, per-order refund, carrier simulation, seller return workflow, modal layout/input protection, authenticated SignalR and reconnect refresh.',
    );
  } finally {
    await browser.close();
    await new Promise((resolve) => provider.close(resolve));
  }
})().catch((e) => {
  console.error(e);
  provider.close();
  process.exitCode = 1;
});
