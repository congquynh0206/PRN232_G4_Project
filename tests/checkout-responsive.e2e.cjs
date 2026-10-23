// Build the frontend first; FRONTEND_DLL can select a separate build output.
// Real Razor checkout with isolated login/cart/quote fixtures; no database or payment calls.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const { chromium } = require('playwright');

const output = path.resolve('artifacts/checkout-responsive-ui');
const items = [
  {
    productId: 1,
    sellerId: 2,
    sellerName: 'Cửa hàng thiết bị điện tử và phụ kiện chính hãng',
    title: 'Tai nghe không dây chống ồn kèm hộp sạc và bộ phụ kiện bảo hành',
    quantity: 2,
    available: 5,
    price: 125,
    weightKg: 0.25,
  },
  {
    productId: 2,
    sellerId: 3,
    sellerName: 'Shop_' + 'TenCuaHangDaiKhongCoKhoangTrang'.repeat(3),
    title: 'SanPham_' + 'MaSanPhamDaiKhongCoKhoangTrang'.repeat(4),
    quantity: 1,
    available: 5,
    price: 75,
    weightKg: 1.2,
  },
];
const quote = {
  subtotal: 325,
  discount: 10,
  shipping: 7.1,
  total: 322.1,
  totalWeightKg: 1.7,
  sellers: items.map((item, index) => ({
    sellerId: item.sellerId,
    quote: {
      subtotal: item.price * item.quantity,
      discount: index ? 0 : 10,
      shipping: index ? 5.1 : 2,
      shippingBase: index ? 5.1 : 2,
      shippingDiscount: 0,
      total: index ? 80.1 : 242,
      totalWeightKg: item.quantity * item.weightKg,
      pricingFingerprint: `checkout-fixture-${item.sellerId}`,
      lines: [
        {
          productId: item.productId,
          originalTotal: item.price * item.quantity,
          sellerDiscount: index ? 0 : 10,
          platformDiscount: 0,
        },
      ],
      promotions: index
        ? []
        : [
            {
              id: 1,
              name: 'Ưu đãi phụ kiện cho khách hàng thân thiết',
              fundingSource: 'Seller',
              amount: 10,
              lines: [{ productId: item.productId }],
            },
          ],
    },
  })),
};

async function listen(server) {
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  return server.address().port;
}

async function main() {
  fs.mkdirSync(output, { recursive: true });
  const loginApi = http.createServer((req, res) => {
    if (req.url !== '/api/auth/login') {
      res.writeHead(404).end();
      return;
    }
    res.setHeader('Content-Type', 'application/json');
    res.end(
      JSON.stringify({
        token: 'checkout-fixture',
        userId: 1,
        username: 'Buyer QA',
        role: 'buyer',
        expiresAt: '2099-01-01T00:00:00Z',
      }),
    );
  });
  const apiPort = await listen(loginApi);
  const reservation = http.createServer();
  const port = await listen(reservation);
  await new Promise((resolve) => reservation.close(resolve));
  const base = `http://127.0.0.1:${port}`;
  const dll = path.resolve(process.env.FRONTEND_DLL || 'frontend/bin/Debug/net8.0/frontend.dll');
  const frontend = spawn('dotnet', [dll, '--urls', base], {
    cwd: path.resolve('frontend'),
    windowsHide: true,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ApiSettings__BaseUrl: `http://127.0.0.1:${apiPort}`,
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  let serverOutput = '',
    browser;
  frontend.stdout.on('data', (chunk) => (serverOutput += chunk));
  frontend.stderr.on('data', (chunk) => (serverOutput += chunk));
  try {
    for (let attempt = 0; ; attempt++) {
      try {
        if ((await fetch(`${base}/Account/Login`)).status === 200) break;
      } catch {}
      if (attempt >= 100) throw new Error(`Frontend did not start: ${serverOutput}`);
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    browser = await chromium.launch({
      headless: true,
      executablePath:
        process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe',
    });
    const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
    const errors = [],
      unexpectedWrites = [];
    page.on('pageerror', (error) => errors.push(error.message));
    await page.route('**/Realtime/Session', (route) => route.fulfill({ status: 503, body: '{}' }));
    await page.route('**/api/proxy/**', (route) => {
      const request = route.request();
      const resource = new URL(request.url()).pathname.replace('/api/proxy/', '');
      if (request.method() !== 'GET' && resource !== 'cart/quote') {
        unexpectedWrites.push(resource);
        return route.fulfill({ status: 409, json: { message: 'Unexpected mutation in UI test' } });
      }
      let json = { items: [], page: 1, pageSize: 10, totalCount: 0, counts: {}, openCount: 0 };
      if (resource === 'addresses')
        json = [
          {
            id: 1,
            fullName: 'Buyer QA',
            street: '123 Đường thử nghiệm',
            city: 'Hà Nội',
            state: 'Hà Nội',
            country: 'Vietnam',
            isDefault: true,
          },
        ];
      if (resource === 'cart') json = { version: 'fixture-v1', items };
      if (resource === 'cart/quote') json = quote;
      return route.fulfill({ json });
    });
    await page.goto(`${base}/Account/Login`);
    await page.locator('#email').fill('buyer@checkout.test');
    await page.locator('#password').fill('fixture-password');
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await page.waitForURL('**/Buyer');
    await page.locator('#continue').click();
    await page.waitForFunction(() => !document.getElementById('pay').disabled);
    assert.equal(await page.locator('#checkout-items .cart-seller').count(), 2);

    for (const width of [320, 360, 390, 414, 520, 600, 760, 768, 820, 900, 901, 1024, 1440]) {
      await page.setViewportSize({ width, height: 1000 });
      await page.locator('input[name="method"][value="paypal"]').check();
      assert.equal(await page.locator('#card-fields').isVisible(), false);
      await page.locator('input[name="method"][value="card"]').check();
      const layout = await page.evaluate(() => {
        const checkout = document.getElementById('checkout');
        const grid = checkout.querySelector('.checkout-grid');
        const main = grid.querySelector('.checkout-main').getBoundingClientRect();
        const total = grid.querySelector('.total-card').getBoundingClientRect();
        const overflowing = [...checkout.querySelectorAll('*')]
          .filter((element) => {
            const box = element.getBoundingClientRect();
            return (
              box.width &&
              (box.right > window.innerWidth + 1 ||
                box.left < -1 ||
                element.scrollWidth > element.clientWidth + 1)
            );
          })
          .map((element) => element.id || element.className || element.tagName);
        return {
          overflowing,
          stacked: total.top >= main.bottom - 1,
          pageWidth: document.documentElement.scrollWidth,
          viewport: window.innerWidth,
        };
      });
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: path.join(output, `checkout-${width}.png`), fullPage: true });
      assert.deepEqual(layout.overflowing, [], `Checkout overflow at ${width}px`);
      assert.ok(layout.pageWidth <= layout.viewport + 1, `Page overflow at ${width}px`);
      assert.equal(layout.stacked, width <= 900, `Checkout columns at ${width}px`);
      assert.equal(await page.locator('#total').textContent(), '$322.10');
      const discountFits = await page
        .locator('#checkout .promotion-breakdown p > b')
        .evaluateAll((amounts) =>
          amounts.every((amount) => {
            const text = document.createRange();
            text.selectNodeContents(amount);
            return text.getClientRects().length === 1;
          }),
        );
      assert.ok(discountFits, `Discount amount wraps at ${width}px`);
      const coupon = page.locator('#coupon-2');
      await coupon.fill('RESPONSIVE-QA');
      await page.locator('[data-apply-coupon="2"]').click();
      await page.waitForFunction(() => !document.getElementById('pay').disabled);
      assert.equal(await coupon.inputValue(), 'RESPONSIVE-QA');
      assert.equal(await page.locator('#coupon-3').inputValue(), '');
      await page.locator('#pay').click({ trial: true });
    }
    await page.locator('#pay').scrollIntoViewIfNeeded();
    assert.deepEqual(errors, []);
    assert.deepEqual(unexpectedWrites, []);
    console.log(
      'Checkout responsive: two sellers, long names, discounts, coupon and totals passed at 320–1440px.',
    );
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
