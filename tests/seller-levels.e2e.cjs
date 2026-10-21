// Dedicated QA app only; prepare its fixture with --levels-sql first.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const base = process.env.G4_LEVEL_E2E_URL;
if (base !== 'http://127.0.0.1:5134') throw new Error('Dedicated QA frontend http://127.0.0.1:5134 required');
const folder = 'artifacts/seller-levels-qa';
(async () => {
  const fixture = JSON.parse(await fs.readFile(folder + '/fixture.json', 'utf8'));
  let browser; try { browser = await chromium.launch({ headless: true }); } catch { browser = await chromium.launch({ headless: true, channel: 'msedge' }); }
  try {
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } }); const page = await ctx.newPage();
    const errors = []; page.on('pageerror', (e) => errors.push(e.message));
    async function login(email) {
      await ctx.clearCookies();
      await page.goto(base + '/Account/Login');
      await page.getByLabel('Địa chỉ thư điện tử', { exact: true }).fill(email);
      await page.getByLabel('Mật khẩu', { exact: true }).fill('LevelDemo123!');
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
      await page.waitForURL((u) => !u.pathname.includes('/Account/Login'));
    }
    await login(fixture.buyerEmail);
    await page.goto(base + '/Buyer?orderId=' + fixture.orderId);
    await page.locator('#detail-content [data-order-detail]').waitFor();
    if (await page.getByRole('button', { name: 'Đánh giá seller', exact: true }).count()) {
      await page.getByRole('button', { name: 'Đánh giá seller', exact: true }).click();
      await page.getByLabel('Mức độ hài lòng', { exact: true }).selectOption('4');
      await page.getByLabel('Nhận xét (không bắt buộc)', { exact: true }).fill('Đơn test: <script>không thực thi</script>');
      await page.getByRole('button', { name: 'Gửi đánh giá', exact: true }).click();
    }
    await page.getByRole('heading', { name: 'Đánh giá seller đã gửi', exact: true }).waitFor();
    assert.equal(await page.locator('.seller-feedback-comment').innerText(), 'Đơn test: <script>không thực thi</script>');
    await page.screenshot({ path: folder + '/buyer-feedback.jpg', fullPage: true });
    console.log('E2E: buyer feedback saved once and rendered as plain text.');
    await login(fixture.sellerEmail);
    await page.locator('[data-tab="seller-levels"]').click();
    await page.locator('#seller-level-detail').getByRole('heading', { name: `Seller #${fixture.sellerId} · Cấp 3`, exact: true }).waitFor();
    await page.getByRole('button', { name: 'Đánh giá lại cấp của tôi', exact: true }).click();
    await page.waitForFunction(() => !document.getElementById('evaluate-seller-level').disabled);
    assert.match(await page.locator('#seller-level-detail').innerText(), /Seller yêu cầu/);
    await page.screenshot({ path: folder + '/seller-levels.jpg', fullPage: true });
    await page.getByText('Thử các tình huống xét cấp · Development', { exact: true }).click();
    for (const [scenario, outcome] of [['level2', 'Cấp 1 → cấp 2'], ['level3', 'Cấp 1 → cấp 3'], ['demote', 'Cấp 3 → cấp 1'], ['sample', 'Cấp 3 → cấp 3'], ['debt', 'Cấp 3 → cấp 1'], ['missing', 'Cấp 3 → cấp 3']]) {
      await page.locator('#level-scenario').selectOption(scenario);
      await page.getByRole('button', { name: 'Chạy mô phỏng', exact: true }).click();
      await page.locator('#level-simulation-result .level-result').filter({ hasText: outcome }).waitFor();
    }
    await page.setViewportSize({ width: 390, height: 844 });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Seller levels mobile overflow');
    await page.locator('#seller-levels').screenshot({ path: folder + '/seller-levels-mobile.jpg' });
    console.log('E2E: seller history, manual evaluation, six simulations and mobile layout passed.');
    await page.setViewportSize({ width: 1280, height: 900 }); await login(fixture.adminEmail);
    await page.locator('[data-tab="seller-levels"]').click();
    await page.locator('#seller-level-filter').getByLabel('Mã seller', { exact: true }).fill(String(fixture.sellerId));
    await page.locator('#seller-level-filter').getByRole('button', { name: 'Lọc', exact: true }).click();
    await page.waitForFunction(() => document.querySelectorAll('#seller-level-list [data-level-seller]').length === 1);
    await page.getByRole('button', { name: 'Xem tiêu chí và lịch sử', exact: true }).click();
    await page.locator('#seller-level-detail').getByRole('heading', { name: `Seller #${fixture.sellerId} · Cấp 3`, exact: true }).waitFor();
    assert.equal(await page.locator('#evaluate-seller-level').count(), 0);
    await page.screenshot({ path: folder + '/admin-levels.jpg', fullPage: true });
    const buyerAuth = await ctx.request.post('http://127.0.0.1:5254/api/auth/login', { data: { email: fixture.buyerEmail, password: 'LevelDemo123!' } });
    assert.equal(buyerAuth.status(), 200); const auth = await buyerAuth.json();
    const denied = await ctx.request.get('http://127.0.0.1:5254/api/admin/seller-levels', { headers: { Authorization: 'Bearer ' + auth.token } });
    assert.equal(denied.status(), 403);
    const repeated = await ctx.request.post(`http://127.0.0.1:5254/api/orders/${fixture.orderId}/seller-feedback`, { headers: { Authorization: 'Bearer ' + auth.token }, data: { rating: 1, comment: 'changed', key: 'new-key' } });
    assert.equal(repeated.status(), 409);
    assert.deepEqual(errors, [], 'Unexpected JavaScript page errors');
    console.log('E2E passed: buyer ownership/review conflict, seller levels/history/simulations, admin readonly detail, role isolation and responsive layout.');
  } finally { await browser.close(); }
})().catch((error) => { console.error(error); process.exitCode = 1; });
