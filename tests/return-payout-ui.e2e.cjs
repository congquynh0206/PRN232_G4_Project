// Chrome fixture QA for real RT1 return dialog and PAY1 seller page/modules.
// API is simulated here; real SQL concurrency/rollback tests are separate.
const fs = require('node:fs'),
  path = require('node:path'),
  assert = require('node:assert/strict');
const { chromium } = require('playwright');
const read = (file) => fs.readFileSync(file, 'utf8');
const output = path.resolve('artifacts/return-window-payout-ui');
(async () => {
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}),
  });
  const dialogs = read('frontend/Views/Shared/_RoleLayout.cshtml')
    .match(/<dialog\b[\s\S]*?<\/dialog>/g)
    .filter((html) => !html.includes('id="navigation-drawer"'))
    .join('');
  try {
    for (const [device, viewport] of [
      ['desktop', { width: 1440, height: 1000 }],
      ['mobile', { width: 390, height: 844 }],
    ]) {
      async function pageFor(body) {
        const context = await browser.newContext({ viewport, timezoneId: 'Asia/Ho_Chi_Minh' });
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', (e) => errors.push(e.message));
        await page.route('http://localhost/workflow-fixture', (r) =>
          r.fulfill({
            contentType: 'text/html',
            body: `<html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><main>${body}</main>${dialogs}</body></html>`,
          }),
        );
        await page.goto('http://localhost/workflow-fixture');
        for (const css of ['checkout', 'roles', 'ui-refresh', 'returns', 'payouts', 'dialogs'])
          await page.addStyleTag({ content: read(`frontend/wwwroot/css/${css}.css`) });
        await page.addScriptTag({ content: read('frontend/wwwroot/js/common.js') });
        return { page, context, errors };
      }
      let view = await pageFor(
        `<div id="message"></div><section class="panel active"><h1>Đơn đã giao</h1><div id="window"></div><button id="open-return">Yêu cầu trả hàng</button></section>${read('frontend/Views/Shared/_ReturnRequestDialog.cshtml')}`,
      );
      let page = view.page;
      await page.evaluate(() => {
        const now = Date.now();
        window.fixtureDeadline = now + 300000;
        window.posts = 0;
        G4.api = async (url, method) => {
          if (method === 'POST') {
            window.posts++;
            return {};
          }
          return {
            orderId: 7,
            reasonCode: 'WrongItem',
            goodsPaid: 100,
            outboundShippingRetained: 0,
            returnShippingFee: 5,
            buyerReturnShippingFee: 0,
            buyerRefund: 105,
            platformSubsidyReversal: 0,
            shippingPayer: 'Seller',
            canRequest: true,
            pricingFingerprint: 'a'.repeat(64),
            returnWindowClosesAt: new Date(fixtureDeadline).toISOString(),
          };
        };
      });
      await page.addScriptTag({ content: read('frontend/wwwroot/js/returns.js') });
      await page.evaluate(() => {
        G4.Returns.init();
        document.getElementById('window').innerHTML = G4.Returns.windowHtml({
          returnWindowClosesAt: new Date(fixtureDeadline).toISOString(),
        });
        document.getElementById('open-return').onclick = () => G4.Returns.open(7);
      });
      await page.locator('#open-return').click();
      await page.locator('#return-confirm:enabled').waitFor();
      assert.match(await page.locator('#return-quote').innerText(), /Hạn mở yêu cầu trả hàng/);
      await page.screenshot({
        path: path.join(output, `${device}-return-before-deadline.png`),
        fullPage: true,
      });
      await page.evaluate(() => {
        Date.now = () => fixtureDeadline;
      });
      await page.locator('#return-confirm:disabled').waitFor();
      assert.match(await page.locator('#return-error').innerText(), /hết hạn/);
      assert.equal(await page.evaluate(() => window.posts), 0);
      assert.doesNotMatch(await page.locator('#return-quote').innerText(), /Phí trả hàng vượt/);
      assert.equal(
        await page.locator('#return-dialog').evaluate((e) => e.scrollWidth > e.clientWidth),
        false,
      );
      await page.screenshot({
        path: path.join(output, `${device}-return-expired.png`),
        fullPage: true,
      });
      assert.deepEqual(view.errors, []);
      await view.context.close();

      for (const returned of [false, true]) {
        const seller = read('frontend/Views/Seller/Index.cshtml')
          .replace(/^@\{[\s\S]*?\}/, '')
          .split('@section Scripts')[0]
          .replace(/<partial\b[^>]*\/>/g, '');
        // This focused payout fixture supplies one navigation entry; the complete
        // rendered sidebar/drawer is exercised by role-navigation.e2e.cjs.
        view = await pageFor(
          `<nav data-role-navigation="seller"><button type="button" data-tab="finance" data-nav-label="Ví & Rút tiền">Ví & Rút tiền</button></nav>${seller}`,
        );
        page = view.page;
        await page.evaluate(() => {
          const noOp = () => {},
            asyncNoOp = async () => {};
          for (const name of [
            'Disputes',
            'SellerDebt',
            'SellerIdentity',
            'SellerLevels',
            'SellerReturns',
            'ProductFeedback',
            'AddressChanges',
            'Promotions',
            'Notifications',
            'IntegrationLogs',
            'SellerReport',
          ])
            G4[name] = { init: noOp, load: asyncNoOp, loadSeller: asyncNoOp };
          G4.Realtime = {
            connected: true,
            subscribe: (topics, callback) => (window.refreshFixture = callback),
          };
          window.fixture = { available: 87.7, payouts: [], calls: [] };
          G4.api = async (url, method, payload) => {
            if (url.startsWith('orders/page'))
              return {
                items: [],
                page: 1,
                pageSize: 10,
                totalCount: 0,
                actionCount: 0,
                counts: {},
              };
            if (url === 'seller/finance')
              return {
                level: 3,
                processing: 0,
                available: fixture.available,
                onHold: 0,
                negative: 0,
                holdSeconds: 0,
                monthlySalesLimit: 1000000,
                monthlySalesAmount: 100,
                levelProgress: {},
                payoutPending: fixture.payouts
                  .filter((p) => !['Completed', 'Returned'].includes(p.status))
                  .reduce((a, p) => a + p.amount, 0),
                paidOut: fixture.payouts
                  .filter((p) => p.status === 'Completed')
                  .reduce((a, p) => a + p.amount, 0),
              };
            if (url.startsWith('seller/finance/transactions/page'))
              return { items: [], page: 1, pageSize: 10, totalCount: 0 };
            if (url.startsWith('seller/finance/funds/page'))
              return { items: [], page: 1, pageSize: 10, totalCount: 0 };
            if (url.startsWith('seller/finance/payouts/page'))
              return {
                items: structuredClone(fixture.payouts),
                page: 1,
                pageSize: 10,
                totalCount: fixture.payouts.length,
              };
            if (url === 'seller/payouts' && method === 'POST') {
              fixture.calls.push(payload);
              fixture.available -= payload.amount;
              const p = {
                id: 1,
                amount: payload.amount,
                currency: 'USD',
                status: 'Created',
                workflowVersion: 1,
                destinationMasked: '****1234',
                createdAt: new Date().toISOString(),
                history: [
                  {
                    fromStatus: null,
                    toStatus: 'Created',
                    actorRole: 'seller',
                    actorUserId: 10,
                    occurredAt: new Date().toISOString(),
                  },
                ],
              };
              fixture.payouts.push(p);
              return structuredClone(p);
            }
            throw new Error('Unexpected fixture request ' + url);
          };
        });
        for (const js of ['role-navigation', 'payouts-ui', 'seller-funds', 'seller'])
          await page.addScriptTag({ content: read(`frontend/wwwroot/js/${js}.js`) });
        await page.locator('[data-tab="finance"]').click();
        await page.getByText('Ví khả dụng', { exact: true }).waitFor();
        await page.locator('#payout-amount').fill('50');
        if (returned) await page.locator('#payout-failure').check();
        await page.locator('#request-payout').click();
        await page.locator('.payout-row').waitFor();
        assert.equal(await page.evaluate(() => fixture.available), 37.7);
        assert.match(await page.locator('#finance-payouts').innerText(), /Đã tạo yêu cầu/);
        assert.match(await page.locator('#finance-balances').innerText(), /Đang chuyển ngân hàng/);
        assert.match(
          await page
            .locator('.balance-card')
            .filter({ hasText: 'Đang chuyển ngân hàng' })
            .innerText(),
          /\$50.00/,
        );
        await page
          .locator('#finance-balances')
          .screenshot({ path: path.join(output, `${device}-${returned}-pending-balances.png`) });
        await page.waitForFunction(
          () => !document.getElementById('message').classList.contains('show'),
        );
        for (const status of ['InProgress', 'FundsSent', returned ? 'Returned' : 'Completed']) {
          await page.evaluate(async (status) => {
            const p = fixture.payouts[0],
              from = p.status;
            p.status = status;
            if (status === 'Returned') fixture.available += p.amount;
            if (status === 'FundsSent') p.bankReferenceId = 'BANK-QA';
            p.history.push({
              fromStatus: from,
              toStatus: status,
              actorRole: 'system',
              occurredAt: new Date().toISOString(),
              bankReferenceId: p.bankReferenceId,
            });
            await window.refreshFixture();
          }, status);
          await page.locator('.payout-row summary').click();
          assert.equal(
            await page.locator('.payout-history li').count(),
            status === 'InProgress' ? 2 : status === 'FundsSent' ? 3 : 4,
          );
          if (status === 'FundsSent')
            assert.match(
              await page.locator('#finance-payouts').innerText(),
              /chưa xác nhận hoàn tất/,
            );
          await page
            .locator('.payout-row')
            .screenshot({ path: path.join(output, `${device}-${returned}-${status}.png`) });
        }
        assert.equal(await page.evaluate(() => fixture.calls.length), 1);
        assert.equal(await page.evaluate(() => fixture.available), returned ? 87.7 : 37.7);
        assert.equal(
          await page.evaluate(() => document.documentElement.scrollWidth > innerWidth),
          false,
        );
        assert.deepEqual(view.errors, []);
        await page
          .locator('#finance-balances')
          .screenshot({ path: path.join(output, `${device}-${returned}-balances.png`) });
        await view.context.close();
      }
    }
    console.log(
      'RT1/PAY1 Chrome fixture passed: deadline/expired quote, real seller request/worker refresh, pending wallet totals, all bank stages/history, completed/returned, desktop/mobile and no overflow',
    );
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
