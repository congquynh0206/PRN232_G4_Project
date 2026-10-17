// Opt-in: dedicated QA frontend/API and G4_ProductFeedback_Test_20261017 only.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const base = process.env.G4_PRODUCT_FEEDBACK_E2E_URL, apiBase = 'http://127.0.0.1:5257/api';
if (base !== 'http://127.0.0.1:5137') throw new Error('Use dedicated product feedback QA frontend at http://127.0.0.1:5137');
const folder = 'artifacts/product-feedback-qa';
(async () => {
  const fixture = JSON.parse(await fs.readFile(folder + '/fixture.json', 'utf8'));
  let browser; try { browser = await chromium.launch({ headless: true }); } catch { browser = await chromium.launch({ headless: true, channel: 'msedge' }); }
  const errors = [];
  async function login(email, role) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } }), page = await context.newPage();
    page.on('pageerror', (e) => errors.push(e.message));
    await page.goto(base + '/Account/Login'); await page.locator('#email').fill(email); await page.locator('#password').fill('FeedbackDemo123!');
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click(); await page.waitForURL(`**/${role}`);
    return { context, page };
  }
  try {
    const buyer = await login(fixture.buyerEmail, 'Buyer'), seller = await login(fixture.sellerEmail, 'Seller');
    const b = buyer.page, s = seller.page;
    await s.locator('[data-tab="product-feedback"]').click();
    await s.locator('#product-feedback-product-status').filter({ hasText: 'sản phẩm phù hợp' }).waitFor();
    await s.waitForFunction(() => document.getElementById('product-feedback-reviews').getAttribute('aria-busy') === 'false' && document.querySelector('[data-feedback-reply]'));
    await s.screenshot({ path: folder + '/seller-desktop.png', fullPage: true });
    await b.goto(base + '/Buyer?orderId=' + fixture.orderId);
    await b.locator('[data-action="product-reviews"]').click();
    const form = b.locator(`[data-product-review="${fixture.productId}"]`);
    await form.locator('select').selectOption('5'); await form.locator('textarea').fill('QA product feedback <script>untrusted()</script>');
    await form.getByRole('button', { name: 'Gửi đánh giá sản phẩm', exact: true }).click();
    await b.locator('#buyer-product-reviews-list .product-review-comment').filter({ hasText: 'QA product feedback' }).waitFor();
    assert.equal(await b.locator('#buyer-product-reviews-list script').count(), 0);
    console.log('E2E: buyer submitted verified review for the purchased product; untrusted content rendered as text.');

    await s.locator('#product-feedback-rating').selectOption('5'); await s.locator('#product-feedback-reply').selectOption('unanswered');
    await s.getByRole('button', { name: 'Áp dụng', exact: true }).click();
    await s.locator('#product-feedback-review-title').filter({ hasText: 'Camera QA' }).waitFor();
    await s.locator('#product-feedback-reviews .product-review-comment').filter({ hasText: 'QA product feedback' }).waitFor();
    const reply = s.locator('[data-feedback-reply]');
    const reviewId = Number(await reply.getAttribute('data-feedback-reply'));
    await reply.locator('textarea').fill('Shop cảm ơn bạn! <img src=x onerror=untrusted()>');
    let dropped = false;
    await s.route('**/api/proxy/seller/product-feedback/reviews/*/reply', async (route) => {
      if (dropped) return route.continue();
      dropped = true; const response = await route.fetch(); assert.equal(response.status(), 200); await route.abort('failed');
    });
    await reply.getByRole('button', { name: 'Gửi phản hồi', exact: true }).click();
    await reply.locator('[role=alert]').filter({ hasText: 'Kết quả chưa rõ' }).waitFor();
    assert.equal(await reply.locator('textarea').isDisabled(), true);
    await reply.getByRole('button', { name: 'Thử lại cùng phản hồi', exact: true }).click();
    await s.locator('#product-feedback-products .empty-note').waitFor();
    console.log('E2E: rating/unanswered filters; lost reply response retried with same request and product leaves unanswered filter.');

    await b.locator('#buyer-product-reviews-refresh').click();
    await b.locator('#buyer-product-reviews-list .product-review-reply p').filter({ hasText: 'Shop cảm ơn bạn!' }).waitFor();
    assert.equal(await b.locator('#buyer-product-reviews-list img[onerror]').count(), 0);
    await b.screenshot({ path: folder + '/buyer-reply-desktop.png', fullPage: true });
    await b.setViewportSize({ width: 390, height: 844 }); await b.screenshot({ path: folder + '/buyer-reply-mobile.png', fullPage: true });
    assert.equal(await b.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true);
    assert.equal(await b.locator('#buyer-product-reviews-dialog').evaluate((d) => d.scrollWidth <= d.clientWidth + 1), true);

    await s.locator('#product-feedback-reset').click();
    await s.locator('#product-feedback-product-status').filter({ hasText: 'sản phẩm phù hợp' }).waitFor();
    await s.locator(`[data-feedback-product="${fixture.productId}"]`).click();
    await s.locator('#product-feedback-reviews .product-review-reply').waitFor();
    await s.setViewportSize({ width: 390, height: 844 }); await s.screenshot({ path: folder + '/seller-mobile.png', fullPage: true });
    assert.equal(await s.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true);
    const draft = s.locator('[data-feedback-reply] textarea'); await draft.fill('Bản nháp không được mất khi có cập nhật nền');
    await s.evaluate(async () => { G4.Realtime.invalidate(['product-feedback']); await G4.Realtime.flush(); });
    assert.equal(await draft.inputValue(), 'Bản nháp không được mất khi có cập nhật nền');

    const auth = async (email) => {
      const response = await buyer.context.request.post(apiBase + '/auth/login', { data: { email, password: 'FeedbackDemo123!' } });
      assert.equal(response.status(), 200); return { Authorization: 'Bearer ' + (await response.json()).token };
    };
    const otherHeaders = await auth(fixture.otherSellerEmail), buyerHeaders = await auth(fixture.buyerEmail);
    assert.equal((await buyer.context.request.post(`${apiBase}/seller/product-feedback/reviews/${reviewId}/reply`, { headers: otherHeaders, data: { comment: 'wrong seller', key: 'wrong-key' } })).status(), 404);
    assert.equal((await buyer.context.request.get(`${apiBase}/seller/product-feedback/products`, { headers: buyerHeaders })).status(), 403);
    const own = await buyer.context.request.get(`${apiBase}/orders/${fixture.orderId}/product-reviews`, { headers: buyerHeaders });
    const saved = (await own.json()).items.find((x) => x.productId === fixture.productId).review;
    assert.equal(saved.reply.comment, 'Shop cảm ơn bạn! <img src=x onerror=untrusted()>');
    assert.deepEqual(errors, []);
    console.log('E2E passed: buyer sees shop reply, desktop/mobile no overflow, draft preserved, cross-role/owner API access rejected, no JS errors.');
  } finally { await browser.close(); }
})().catch((e) => { console.error(e); process.exitCode = 1; });
