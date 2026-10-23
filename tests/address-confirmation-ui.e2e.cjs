// Real module UI with a shared API fixture; database/transaction QA runs separately.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const read = (file) => fs.readFileSync(file, 'utf8');
const output = path.resolve('artifacts/address-confirmation-ui');
(async () => {
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}),
  });
  try {
    for (const [device, viewport] of [
      ['desktop', { width: 1440, height: 1000 }],
      ['mobile', { width: 390, height: 844 }],
    ]) {
      for (const approve of [true, false]) {
        const shipment = {
          id: 1,
          orderId: 42,
          shipperId: 7,
          direction: 'Outbound',
          status: 'InTransit',
          deliveryAttempts: 0,
          trackingNumber: 'AD1-42',
          pickupAddressSnapshot: 'Seller, Hanoi',
          deliveryAddressSnapshot: 'Original address',
        };
        const order = {
          id: 42,
          status: 'Shipping',
          addressSnapshot: 'Original address',
          addressChangeCount: 0,
          totalWeightKg: 1.2,
          canRequestAddressChange: true,
          addressChanges: [],
          shipments: [shipment],
          events: [],
        };
        const calls = [],
          errors = [];
        async function api(url, method, payload) {
          calls.push({ url, method, payload });
          if (url === 'addresses')
            return [
              {
                fullName: 'Buyer',
                street: 'New street <img src=x>',
                city: 'Hanoi',
                state: 'Hanoi',
                country: 'Vietnam',
                isDefault: true,
              },
            ];
          if (url === 'orders/42') return structuredClone(order);
          if (url.startsWith('shipper/shipments/page'))
            return {
              items: [
                {
                  ...shipment,
                  pickupAddress: shipment.pickupAddressSnapshot,
                  deliveryAddress: shipment.deliveryAddressSnapshot,
                },
              ],
              page: 1,
              pageSize: 10,
              totalCount: 1,
              pendingCount: 0,
              mineCount: 1,
            };
          if (url === 'orders/42/address-changes/quote')
            return {
              token: 'fixture-quote',
              oldAddress: order.addressSnapshot,
              newAddress: Object.values(payload.address).join(', '),
              oldShippingCost: 5.2,
              newShippingCost: 2.1,
              sellerCap: 0,
              buyerRefund: 3.1,
              shipperId: 7,
              withinCap: true,
            };
          if (url === 'orders/42/address-changes') {
            assert.equal(payload.quoteToken, 'fixture-quote');
            order.addressChanges = [
              {
                id: 12,
                orderId: 42,
                workflowVersion: 1,
                status: 'Pending',
                reason: payload.reason,
                oldAddress: order.addressSnapshot,
                newAddress: Object.values(payload.address).join(', '),
                oldShippingCost: 5.2,
                newShippingCost: 2.1,
                sellerCap: 0,
                buyerRefund: 3.1,
                expectedShipperId: 7,
                createdAt: new Date().toISOString(),
                expiresAt: new Date(Date.now() + 60000).toISOString(),
              },
            ];
            order.canRequestAddressChange = false;
            return structuredClone(order.addressChanges[0]);
          }
          const r = order.addressChanges[0];
          if (url === 'seller/address-changes/12/decision') {
            assert.equal(payload.approve, true);
            r.status = 'AwaitingShipper';
            r.sellerConfirmedAt = new Date().toISOString();
            return structuredClone(r);
          }
          if (url === 'shipper/shipments/1/address-change/decision') {
            assert.equal(payload.requestId, 12);
            assert.equal(payload.approve, approve);
            if (!approve) assert.ok(payload.reason.trim());
            r.status = approve ? 'Approved' : 'Rejected';
            r.shipperDecidedAt = new Date().toISOString();
            if (approve) {
              r.appliedAt = r.shipperDecidedAt;
              order.addressSnapshot = r.newAddress;
              order.addressChangeCount = 1;
              shipment.deliveryAddressSnapshot = r.newAddress;
              shipment.addressChangeRequestId = 12;
              shipment.addressChangeAcknowledgedAt = r.appliedAt;
            }
            return structuredClone(r);
          }
          throw new Error('Unexpected request ' + url);
        }
        async function createPage(role) {
          const context = await browser.newContext({ viewport, timezoneId: 'Asia/Ho_Chi_Minh' });
          const page = await context.newPage();
          page.on('pageerror', (e) => errors.push(e.message));
          const dialogs = read('frontend/Views/Shared/_RoleLayout.cshtml')
            .match(/<dialog\b[\s\S]*?<\/dialog>/g)
            .join('');
          const body =
            role === 'shipper'
              ? read('frontend/Views/Shipper/Index.cshtml')
                  .replace(/^@\{[\s\S]*?\}/, '')
                  .split('@section Scripts')[0]
              : '<div id="message" role="status"></div><section class="panel active" id="fixture-summary"></section>';
          await page.route('http://localhost/ad1-fixture', (route) =>
            route.fulfill({
              contentType: 'text/html',
              body: `<html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body class="${role === 'shipper' ? 'vnpost-workspace' : ''}"><main>${body}</main>${dialogs}</body></html>`,
            }),
          );
          await page.goto('http://localhost/ad1-fixture');
          for (const css of [
            'checkout',
            'roles',
            'ui-refresh',
            'address-changes',
            'vnpost',
            'dialogs',
          ])
            await page.addStyleTag({ content: read(`frontend/wwwroot/css/${css}.css`) });
          await page.exposeFunction('fixtureApi', api);
          await page.addScriptTag({ content: read('frontend/wwwroot/js/common.js') });
          await page.evaluate(() => {
            G4.api = (url, method, payload) => window.fixtureApi(url, method, payload);
          });
          await page.addScriptTag({ content: read('frontend/wwwroot/js/address-changes.js') });
          if (role === 'shipper') {
            for (const js of ['shipper-ui', 'shipper'])
              await page.addScriptTag({ content: read(`frontend/wwwroot/js/${js}.js`) });
          } else
            await page.evaluate(async (role) => {
              const render = async () =>
                (document.getElementById('fixture-summary').innerHTML =
                  G4.AddressChanges.summaryHtml(await G4.api('orders/42'), role));
              G4.AddressChanges.init(role, render);
              await render();
            }, role);
          return { page, context };
        }
        let view = await createPage('buyer'),
          page = view.page;
        await page.locator('[data-address-change="request"]').click();
        await page.locator('#action-reason').fill('Recipient moved <script>alert(1)</script>');
        await page.locator('#action-confirm').click();
        await page.waitForFunction(
          () =>
            document.getElementById('action-title').textContent === 'Xác nhận báo giá đổi địa chỉ',
        );
        assert.equal(calls.filter((x) => x.url === 'orders/42/address-changes').length, 0);
        assert.match(await page.locator('#action-context').innerText(), /Buyer không trả thêm/);
        assert.equal(await page.locator('#action-context img').count(), 0);
        await page.screenshot({
          path: path.join(output, `${device}-${approve}-buyer-quote.png`),
          fullPage: true,
        });
        await page.locator('#action-confirm').click();
        await page.waitForFunction(() => !document.getElementById('action-dialog').open);
        await page.locator('.address-change-entry').waitFor();
        assert.equal(order.addressSnapshot, 'Original address');
        await view.context.close();
        view = await createPage('seller');
        page = view.page;
        await page.locator('[data-address-change="approve"]').click();
        await page.locator('#action-confirm').click();
        await page.getByText('Chờ shipper chấp thuận', { exact: true }).waitFor();
        assert.equal(order.addressSnapshot, 'Original address');
        assert.equal(order.addressChangeCount, 0);
        await page.screenshot({
          path: path.join(output, `${device}-${approve}-seller-waiting.png`),
          fullPage: true,
        });
        await view.context.close();
        view = await createPage('shipper');
        page = view.page;
        await page.locator('[data-open-order="42"]').click();
        assert.equal(await page.locator('[data-event="OutForDelivery"]').count(), 0);
        assert.match(await page.locator('#detail-content').innerText(), /chưa có hiệu lực/);
        assert.equal(await page.locator('#detail-content script, #detail-content img').count(), 0);
        await page.screenshot({
          path: path.join(output, `${device}-${approve}-shipper-waiting.png`),
          fullPage: true,
        });
        await page.locator(`[data-address-shipper="${approve ? 'approve' : 'reject'}"]`).click();
        if (!approve) await page.locator('#action-reason').fill('Outside delivery area');
        await page.locator('#action-confirm').click();
        await page.locator('[data-event="OutForDelivery"]:not([data-custom])').waitFor();
        assert.equal(order.addressChangeCount, approve ? 1 : 0);
        assert.equal(order.addressSnapshot === 'Original address', !approve);
        assert.equal(
          await page.evaluate(() => document.documentElement.scrollWidth > innerWidth),
          false,
        );
        assert.equal(
          await page.locator('#detail-dialog').evaluate((e) => e.scrollWidth > e.clientWidth),
          false,
        );
        assert.deepEqual(errors, []);
        await page.screenshot({
          path: path.join(output, `${device}-${approve}-shipper-final.png`),
          fullPage: true,
        });
        await view.context.close();
      }
    }
    console.log(
      'AD1 Chrome fixture QA passed: buyer quote-before-submit, seller wait, assigned shipper accept/refuse, old address until final approval, escaped input, delivery action, desktop/mobile',
    );
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
