// Browser fixture QA using the real Razor body, dialogs, CSS and JavaScript.
// API is simulated; SQL transaction/concurrency coverage is in ShipmentReattemptSqlChecks.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const read = (file) => fs.readFileSync(file, 'utf8');
const output = path.resolve('artifacts/shipping-reattempt-ui');

(async () => {
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({ headless: true,
    ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  try {
    for (const direction of ['Outbound', 'Return']) {
      for (const [device, viewport] of [['desktop', { width: 1440, height: 1000 }], ['mobile', { width: 390, height: 844 }]]) {
        const context = await browser.newContext({ viewport, timezoneId: 'Asia/Ho_Chi_Minh' });
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', (e) => errors.push(e.message));
        const body = read('frontend/Views/Shipper/Index.cshtml').replace(/^@\{[\s\S]*?\}/, '').split('@section Scripts')[0];
        const dialogs = read('frontend/Views/Shared/_RoleLayout.cshtml').match(/<dialog\b[\s\S]*?<\/dialog>/g).join('');
        // localhost is a secure context, matching the app and allowing crypto.randomUUID.
        await page.route('http://localhost/sh1-fixture', (route) => route.fulfill({ contentType: 'text/html',
          body: `<html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body class="vnpost-workspace"><main>${body}</main>${dialogs}</body></html>` }));
        await page.goto('http://localhost/sh1-fixture');
        for (const css of ['checkout', 'roles', 'ui-refresh', 'address-changes', 'vnpost', 'dialogs'])
          await page.addStyleTag({ content: read(`frontend/wwwroot/css/${css}.css`) });
        await page.addScriptTag({ content: read('frontend/wwwroot/js/common.js') });
        await page.evaluate((direction) => {
          const shipment = { id: 1, orderId: 42, shipperId: 7, trackingNumber: 'SH1-42-' + direction,
            direction, status: 'DeliveryFailed', deliveryAttempts: 1, totalWeightKg: 1.2,
            pickupAddress: 'Điểm gửi ban đầu', deliveryAddress: 'Điểm nhận ban đầu',
            pickupAddressSnapshot: 'Điểm gửi ban đầu', deliveryAddressSnapshot: 'Điểm nhận ban đầu' };
          const events = [{ shippingInfoId: 1, status: 'DeliveryFailed', occurredAt: new Date().toISOString(), actorUserId: 7, actorRole: 'shipper', note: 'Người nhận hẹn giao lại' }];
          window.fixture = { shipment, events, calls: [], failNext: true };
          G4.AddressChanges = { requiresAck: () => false, shipperNotice: () => '' };
          G4.api = async (url, method, payload) => {
            if (url.startsWith('shipper/shipments/page')) return structuredClone({ items: [shipment], page: 1, pageSize: 10, totalCount: 1, pendingCount: 0, mineCount: 1 });
            if (url === 'orders/42') return structuredClone({ id: 42, status: 'Shipping', totalWeightKg: 1.2, shipments: [shipment], events });
            if (url === 'shipments/1/events' && method === 'POST') {
              fixture.calls.push(structuredClone(payload));
              if (fixture.failNext) { fixture.failNext = false; throw new Error('Kết nối gián đoạn, hãy thử lại'); }
              shipment.status = payload.status;
              if (payload.status === 'ReattemptScheduled') { shipment.reattemptAt = payload.reattemptAt; shipment.reattemptReason = payload.note; }
              if (payload.status === 'OutForDelivery') shipment.deliveryAttempts++;
              events.push({ ...payload, shippingInfoId: 1, actorUserId: 7, actorRole: 'shipper', occurredAt: new Date().toISOString() });
              return { applied: true };
            }
            throw new Error('Unexpected fixture request ' + url);
          };
        }, direction);
        for (const script of ['shipper-ui', 'shipper']) await page.addScriptTag({ content: read(`frontend/wwwroot/js/${script}.js`) });
        await page.locator('[data-open-order="42"]').click();
        assert.equal(await page.locator('[data-event="OutForDelivery"]').count(), 0);
        await page.locator('[data-event="ReattemptScheduled"]').click();
        await page.locator('#action-confirm').click();
        assert.equal(await page.locator('#action-dialog').evaluate((e) => e.open), true);
        await page.locator('#action-reattemptAt').fill('2020-01-01T10:00');
        await page.locator('#action-note').fill('   ');
        await page.locator('#action-confirm').click();
        assert.equal(await page.locator('#action-dialog').evaluate((e) => e.open), true);
        const local = await page.evaluate(() => {
          const d = new Date(Date.now() + 86400000);
          return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
        });
        const reason = 'Người nhận hẹn lại <script>window.injected=true</script>';
        await page.locator('#action-reattemptAt').fill(local);
        await page.locator('#action-note').fill(reason);
        await page.screenshot({ path: path.join(output, `${direction}-${device}-form.png`), fullPage: true });
        await page.locator('#action-confirm').click();
        await page.waitForFunction(() => fixture.calls.length === 1);
        await page.waitForFunction(() => !document.querySelector('[data-event="ReattemptScheduled"]').disabled);
        // Unknown outcome must reuse exactly the same event ID and payload.
        await page.locator('[data-event="ReattemptScheduled"]').click();
        await page.waitForFunction(() => fixture.calls.length === 2 && document.querySelector('[data-event="OutForDelivery"]'));
        const state = await page.evaluate(() => ({ calls: fixture.calls, shipment: fixture.shipment, injected: window.injected }));
        assert.deepEqual(state.calls[0], state.calls[1]);
        assert.match(state.calls[0].reattemptAt, /Z$/);
        assert.equal(state.calls[0].actorUserId, undefined);
        assert.equal(state.calls[0].actorRole, undefined);
        assert.equal(state.calls[0].location, ''); // Schedule need not invent a physical location.
        assert.equal(state.shipment.deliveryAttempts, 1);
        assert.equal(state.injected, undefined);
        assert.equal(await page.locator('#action-dialog').evaluate((e) => e.open), false);
        await page.locator('[data-detail-tab="history"]').click();
        assert.match(await page.locator('#vn-pane-history').innerText(), /Shipper #7/);
        assert.match(await page.locator('#vn-pane-history').innerText(), /Dự kiến giao lại/);
        assert.equal(await page.locator('#vn-pane-history script').count(), 0);
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
        assert.equal(await page.locator('#detail-dialog').evaluate((e) => e.scrollWidth > e.clientWidth), false);
        await page.screenshot({ path: path.join(output, `${direction}-${device}-scheduled.png`), fullPage: true });
        await page.locator('[data-event="OutForDelivery"]:not([data-custom])').click();
        await page.waitForFunction(() => fixture.shipment.deliveryAttempts === 2);
        assert.deepEqual(errors, []);
        await context.close();
      }
    }
    console.log('SH1 browser fixture QA passed: both directions, desktop/mobile, required/future validation, UTC, retry key, actor/timeline, escaped notes, manual start and no horizontal overflow');
  } finally { await browser.close(); }
})().catch((error) => { console.error(error); process.exitCode = 1; });
